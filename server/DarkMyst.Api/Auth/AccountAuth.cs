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
    /// Bearer-token auth, stub-simple on purpose (docs/06-roadmap.md phase D scope: "Bearer-token
    /// stub for auth; do not design something real auth cannot slot into later"). A token is an
    /// opaque, random, database-looked-up string — nothing here assumes it is ever a signed JWT or
    /// anything else a real provider would need to change; swapping in real Apple/Google Sign-In
    /// later only changes how <see cref="Accounts.IIdentityProvider"/> issues one, not how a
    /// request is authenticated against it.
    /// </summary>
    public static class AccountAuth
    {
        private const string Prefix = "Bearer ";

        public static async Task<AccountEntity> RequireAccountAsync(HttpContext http, ApiDbContext db, CancellationToken ct)
        {
            string header = http.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Missing or malformed 'Authorization: Bearer <token>' header.");
            }

            string token = header.Substring(Prefix.Length).Trim();
            if (token.Length == 0)
            {
                throw new UnauthorizedAccessException("Empty bearer token.");
            }

            AccountEntity account = await db.Accounts.FirstOrDefaultAsync(a => a.AccessToken == token, ct);
            if (account == null)
            {
                // Covers both a never-issued token and a superseded account's token, which
                // LinkingService.ConfirmAsync clears to null — neither can ever match a row.
                throw new UnauthorizedAccessException("Invalid or expired bearer token.");
            }

            account.LastSeenAt = DateTimeOffset.UtcNow;
            // Committed immediately, ahead of whatever transaction the endpoint itself opens next
            // (e.g. IdempotencyService.ExecuteAsync) — "was this account recently seen" is a
            // best-effort convenience, not something that needs to share a mutation's atomicity.
            await db.SaveChangesAsync(ct);
            return account;
        }
    }
}
