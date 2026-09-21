using System;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Accounts;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;

namespace DarkMyst.Api.Admin
{
    /// <summary>
    /// Issues admin identities the same honest way <see cref="Accounts.StubIdentityProvider"/>
    /// issues player ones: no real verification, trusted at face value. This is deliberately as
    /// simple as <see cref="AccountService.CreateGuestAsync"/> — the thing that is not simple is
    /// <c>Auth/AdminAuth.cs</c>, the gate every other admin endpoint goes through.
    /// <para>
    /// <c>POST /admin/bootstrap</c> (the only caller) is gated in Program.cs the same way the
    /// existing <c>/debug/grant-*</c> endpoints are: on outside of <c>Development</c> or
    /// <c>Admin:AllowBootstrap</c>. Nothing here is a path to production admin access — real
    /// admin SSO is phase E, alongside real player SSO (docs/06-roadmap.md).
    /// </para>
    /// </summary>
    public sealed class AdminAccountService
    {
        private readonly ApiDbContext _db;

        public AdminAccountService(ApiDbContext db)
        {
            _db = db;
        }

        public async Task<AdminAccountEntity> BootstrapAsync(string name, CancellationToken ct)
        {
            var admin = new AdminAccountEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                Name = string.IsNullOrWhiteSpace(name) ? "dev-admin" : name,
                AccessToken = AccountService.NewToken(),
                CreatedAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            };

            _db.AdminAccounts.Add(admin);
            await _db.SaveChangesAsync(ct);
            return admin;
        }
    }
}
