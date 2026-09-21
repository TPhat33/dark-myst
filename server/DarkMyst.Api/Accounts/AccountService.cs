using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Accounts
{
    public sealed class AccountService
    {
        private readonly ApiDbContext _db;

        public AccountService(ApiDbContext db)
        {
            _db = db;
        }

        public static string NewToken()
        {
            // 256 bits, URL-safe. Opaque on purpose — nothing about an account can be derived from
            // its token, which is what lets this same scheme keep working once a real identity
            // provider sits in front of it (docs/10-backend-spec.md).
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public async Task<AccountEntity> CreateGuestAsync(CancellationToken ct)
        {
            var account = new AccountEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                Kind = AccountKind.Guest,
                AccessToken = NewToken(),
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);
            return account;
        }

        public Task<AccountEntity> FindByTokenAsync(string token, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Task.FromResult<AccountEntity>(null);
            }

            return _db.Accounts.FirstOrDefaultAsync(a => a.AccessToken == token, ct);
        }

        /// <summary>A quick summary of an account's save, exactly what
        /// docs/04-economy-spec.md requires be shown when a link conflict is presented: "เลเวลสูงสุด
        /// จำนวนตัวละคร เวลาเล่นล่าสุด" (highest level, character count, last played).</summary>
        public async Task<AccountSaveSummary> SummarizeAsync(string accountId, CancellationToken ct)
        {
            var characters = await _db.Characters.AsNoTracking().Where(c => c.OwnerId == accountId).ToListAsync(ct);
            AccountEntity account = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);

            int highestLevel = 0;
            foreach (OwnedCharacterEntity c in characters)
            {
                if (c.Level > highestLevel)
                {
                    highestLevel = c.Level;
                }
            }

            return new AccountSaveSummary(accountId, characters.Count, highestLevel, account.Gold, account.LastSeenAt, account.CreatedAt);
        }
    }

    public sealed record AccountSaveSummary(
        string AccountId, int CharacterCount, int HighestLevel, int Gold, DateTimeOffset LastSeenAt, DateTimeOffset CreatedAt);
}
