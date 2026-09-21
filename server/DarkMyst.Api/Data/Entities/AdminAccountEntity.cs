using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// One admin identity — deliberately a completely separate table from
    /// <see cref="AccountEntity"/>, not a role flag on it. A player's bearer token is looked up in
    /// <c>accounts</c>; an admin token is looked up here, in <c>admin_accounts</c>, via a different
    /// request header (<c>X-Admin-Token</c>, see <c>Auth/AdminAuth.cs</c>). Structurally, a player
    /// token can never satisfy the admin gate: even a byte-for-byte identical string would still
    /// need a matching row in this table, and access tokens are 256-bit random
    /// (<see cref="Accounts.AccountService.NewToken"/>-style), so a collision is not a practical
    /// concern either.
    /// <para>
    /// Identity here is stubbed exactly as honestly as <see cref="Accounts.IIdentityProvider"/>
    /// stubs a player's: <c>POST /admin/bootstrap</c> hands out a token to whoever can reach it, no
    /// real SSO. What is not stubbed is the gate itself — every other admin endpoint requires a
    /// token that actually exists in this table. Real Apple/Google-style admin SSO is phase E, same
    /// as the player-facing one (docs/10-backend-spec.md, docs/06-roadmap.md).
    /// </para>
    /// </summary>
    public sealed class AdminAccountEntity
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string AccessToken { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset LastSeenAt { get; set; }
    }
}
