using System;
using System.Collections.Generic;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>How this account came to exist / whether it has a durable identity behind it.</summary>
    public enum AccountKind
    {
        /// <summary>Created on demand, no durable identity linked yet. Can be superseded by a link.</summary>
        Guest = 0,

        /// <summary>Has at least one linked identity (see <see cref="LinkedIdentityEntity"/>).</summary>
        Linked = 1,

        /// <summary>
        /// Lost a link-conflict decision (docs/04-economy-spec.md: "a guest save on a new device
        /// linking to an account that already has a save"). Its data is kept, not deleted, so the
        /// player can be shown it again if they chose wrong — it just stops being anyone's active
        /// account and can no longer authenticate or be mutated.
        /// </summary>
        Superseded = 2
    }

    /// <summary>
    /// One player account. This is the root everything else (characters, inventory, runs, the
    /// ledger) hangs off of. Bearer tokens are opaque, random, and stored here rather than derived
    /// from anything guessable — swapping in real OAuth later only replaces how a token gets
    /// issued, not how a request is authenticated against one (see docs/10-backend-spec.md).
    /// </summary>
    public sealed class AccountEntity
    {
        public string Id { get; set; }

        public AccountKind Kind { get; set; }

        /// <summary>
        /// Maintained balance, always written in the same transaction as the
        /// <see cref="LedgerEntryEntity"/> row that changed it — never derived from the ledger on
        /// read. docs/07-testing-plan.md's "ตรวจย้อนหลังได้" is proven by a test that re-sums the
        /// ledger and checks it against this column, not by defining this column as that sum.
        /// </summary>
        public int Gold { get; set; }

        /// <summary>
        /// Premium currency ("อัญมณี", docs/04-economy-spec.md) — added with the summon system
        /// since it is the first thing in this API that actually spends gems (`POST /summon/pull`).
        /// Maintained the same way <see cref="Gold"/> is: always written in the same transaction as
        /// the <see cref="LedgerEntryEntity"/> row that changed it, via
        /// <see cref="Ledger.LedgerService.ApplyGems"/>. There is still no real purchase flow
        /// (docs/10-backend-spec.md "deliberately deferred") — <c>/debug/grant-gems</c> is this
        /// currency's stand-in, same as <c>/debug/grant-gold</c> is for gold.
        /// </summary>
        public int Gems { get; set; }

        /// <summary>Bearer token clients present as `Authorization: Bearer {token}`. Null once the
        /// account is <see cref="AccountKind.Superseded"/> — a superseded account can never
        /// authenticate again, only be read back during a link decision.</summary>
        public string AccessToken { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset LastSeenAt { get; set; }

        public List<OwnedCharacterEntity> Characters { get; set; } = new List<OwnedCharacterEntity>();

        public List<InventoryMaterialEntity> Materials { get; set; } = new List<InventoryMaterialEntity>();
    }
}
