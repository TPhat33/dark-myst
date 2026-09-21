using System;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Accounts
{
    public abstract class LinkOutcome
    {
        /// <summary>The identity had no existing account, or already pointed at this same guest —
        /// linking happened immediately, no decision needed.</summary>
        public sealed class Linked : LinkOutcome
        {
            public Linked(string accountId)
            {
                AccountId = accountId;
            }

            public string AccountId { get; }
        }

        /// <summary>
        /// docs/04-economy-spec.md's called-out case: "ผู้เล่นมีเซฟ guest บนเครื่องใหม่ แล้วเชื่อม
        /// กับบัญชีถาวรที่มีเซฟเดิมอยู่แล้ว" — the identity already belongs to a different account
        /// than the guest asking to link. Nothing is touched yet; both saves are handed back so the
        /// player can choose, and the choice is what <see cref="LinkingService.Confirm"/> commits.
        /// </summary>
        public sealed class ConflictPending : LinkOutcome
        {
            public ConflictPending(string pendingLinkId, AccountSaveSummary guestSave, AccountSaveSummary existingSave)
            {
                PendingLinkId = pendingLinkId;
                GuestSave = guestSave;
                ExistingSave = existingSave;
            }

            public string PendingLinkId { get; }

            public AccountSaveSummary GuestSave { get; }

            public AccountSaveSummary ExistingSave { get; }
        }
    }

    public enum LinkChoice
    {
        /// <summary>Keep playing on the guest account's save; the identity moves to it. The
        /// existing durable account is superseded — kept, not deleted.</summary>
        KeepGuestSave,

        /// <summary>Keep the existing durable account's save; the guest account is superseded.</summary>
        KeepExistingSave
    }

    /// <summary>
    /// Implements the account-linking flow docs/04-economy-spec.md requires, including the one
    /// case it calls out by name. See <see cref="LinkOutcome"/> for the two shapes a link attempt
    /// can take.
    /// </summary>
    public sealed class LinkingService
    {
        private static readonly TimeSpan PendingLinkLifetime = TimeSpan.FromDays(7);

        private readonly ApiDbContext _db;
        private readonly AccountService _accounts;

        public LinkingService(ApiDbContext db, AccountService accounts)
        {
            _db = db;
            _accounts = accounts;
        }

        public async Task<LinkOutcome> StartAsync(string guestAccountId, ExternalIdentity identity, CancellationToken ct)
        {
            LinkedIdentityEntity link = await _db.LinkedIdentities
                .FirstOrDefaultAsync(l => l.Provider == identity.Provider && l.ExternalId == identity.ExternalId, ct);

            if (link == null)
            {
                // Nobody has ever linked this identity before — attach it to the asking account
                // outright, nothing to choose between.
                _db.LinkedIdentities.Add(new LinkedIdentityEntity
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Provider = identity.Provider,
                    ExternalId = identity.ExternalId,
                    AccountId = guestAccountId,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                AccountEntity account = await _db.Accounts.FirstAsync(a => a.Id == guestAccountId, ct);
                account.Kind = AccountKind.Linked;

                await _db.SaveChangesAsync(ct);
                return new LinkOutcome.Linked(guestAccountId);
            }

            if (link.AccountId == guestAccountId)
            {
                // Re-linking the same pair — idempotent no-op, not a conflict.
                return new LinkOutcome.Linked(guestAccountId);
            }

            // The identity already belongs to a different account. Do not touch either account —
            // hand back both saves' summaries and let the caller confirm a choice.
            var pending = new PendingLinkEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                GuestAccountId = guestAccountId,
                ExistingAccountId = link.AccountId,
                Provider = identity.Provider,
                ExternalId = identity.ExternalId,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow + PendingLinkLifetime
            };
            _db.PendingLinks.Add(pending);
            await _db.SaveChangesAsync(ct);

            AccountSaveSummary guestSave = await _accounts.SummarizeAsync(guestAccountId, ct);
            AccountSaveSummary existingSave = await _accounts.SummarizeAsync(link.AccountId, ct);
            return new LinkOutcome.ConflictPending(pending.Id, guestSave, existingSave);
        }

        /// <summary>
        /// Commits a pending link's decision. The losing account's row is set to
        /// <see cref="AccountKind.Superseded"/> and its <see cref="AccountEntity.AccessToken"/> is
        /// cleared so it can never authenticate again — but every character, material and gold row
        /// it owns is left exactly as it was. "เก็บเซฟที่ถูกทิ้งไว้ระยะหนึ่งเผื่อเลือกผิด" (docs/04)
        /// means retained, not deleted; nothing about this method deletes anything.
        /// </summary>
        public async Task<AccountEntity> ConfirmAsync(string pendingLinkId, LinkChoice choice, CancellationToken ct)
        {
            PendingLinkEntity pending = await _db.PendingLinks.FirstOrDefaultAsync(p => p.Id == pendingLinkId, ct);
            if (pending == null)
            {
                throw new InvalidOperationException("No pending link '" + pendingLinkId + "'.");
            }

            if (pending.ResolvedAt.HasValue)
            {
                throw new InvalidOperationException("Pending link '" + pendingLinkId + "' was already resolved.");
            }

            if (DateTimeOffset.UtcNow > pending.ExpiresAt)
            {
                throw new InvalidOperationException("Pending link '" + pendingLinkId + "' has expired.");
            }

            string winnerId = choice == LinkChoice.KeepGuestSave ? pending.GuestAccountId : pending.ExistingAccountId;
            string loserId = choice == LinkChoice.KeepGuestSave ? pending.ExistingAccountId : pending.GuestAccountId;

            AccountEntity winner = await _db.Accounts.FirstAsync(a => a.Id == winnerId, ct);
            AccountEntity loser = await _db.Accounts.FirstAsync(a => a.Id == loserId, ct);

            winner.Kind = AccountKind.Linked;

            loser.Kind = AccountKind.Superseded;
            loser.AccessToken = null;

            LinkedIdentityEntity link = await _db.LinkedIdentities
                .FirstAsync(l => l.Provider == pending.Provider && l.ExternalId == pending.ExternalId, ct);
            link.AccountId = winnerId;

            pending.ResolvedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            return winner;
        }
    }
}
