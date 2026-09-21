using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// Maps one durable external identity (stubbed provider — see
    /// <see cref="Accounts.IIdentityProvider"/>) to the account it currently authenticates into.
    /// A row here is the durable side of account linking; a guest account has none until it links.
    /// </summary>
    public sealed class LinkedIdentityEntity
    {
        public string Id { get; set; }

        public string Provider { get; set; }

        public string ExternalId { get; set; }

        public string AccountId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// A link attempt that found the external identity already pointing at a different account
    /// than the one asking to link. Both saves are shown to the player so they can choose; this
    /// row records that choice is still pending, and which two accounts are candidates. Neither
    /// account is touched until <see cref="Accounts.LinkingService.Confirm"/> runs.
    /// </summary>
    public sealed class PendingLinkEntity
    {
        public string Id { get; set; }

        public string GuestAccountId { get; set; }

        public string ExistingAccountId { get; set; }

        public string Provider { get; set; }

        public string ExternalId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? ResolvedAt { get; set; }
    }
}
