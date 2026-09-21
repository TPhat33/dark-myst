using System;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api
{
    /// <summary>
    /// The admin authorization gate — deliberately not a re-use of
    /// <see cref="AccountAuth.RequireAccountAsync"/>. Every admin endpoint calls this instead,
    /// which:
    /// <list type="bullet">
    /// <item>reads a completely different header (<c>X-Admin-Token</c>, never
    /// <c>Authorization</c>), so a client that forwards its normal player bearer token on every
    /// request never even presents a credential this gate looks at;</item>
    /// <item>looks the token up in <c>admin_accounts</c>, a table a player token was never issued
    /// into, so even a client that deliberately copies its player token into
    /// <c>X-Admin-Token</c> still gets refused.</item>
    /// </list>
    /// Both properties are proven directly by
    /// <c>tests/DarkMyst.Api.Tests/AdminAuthTests.cs</c> (docs/11-admin-spec.md).
    /// </summary>
    public static class AdminAuth
    {
        public const string HeaderName = "X-Admin-Token";

        public static async Task<AdminAccountEntity> RequireAdminAsync(HttpContext http, ApiDbContext db, CancellationToken ct)
        {
            string token = http.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new UnauthorizedAccessException("Missing '" + HeaderName + "' header.");
            }

            AdminAccountEntity admin = await db.AdminAccounts.FirstOrDefaultAsync(a => a.AccessToken == token, ct);
            if (admin == null)
            {
                throw new UnauthorizedAccessException("Invalid admin token.");
            }

            admin.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return admin;
        }
    }
}
