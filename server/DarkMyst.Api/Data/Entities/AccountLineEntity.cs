using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// Records that an account has ever owned a stage-I character of line <see cref="LineId"/> —
    /// the "has this line before" check a summon pull needs on every roll to decide first-copy
    /// vs. duplicate (docs/12-summon-spec.md §"ตัวซ้ำต้องไม่มีวันเป็นของเหลือ").
    /// <para>
    /// Design choice: a small dedicated table, maintained alongside every grant that creates a
    /// stage-I <see cref="OwnedCharacterEntity"/> (summon, spark-redeem — <c>Debug/DebugGrants.cs</c>
    /// deliberately does not touch this table, see its own remarks), rather than deriving "owns a
    /// line" by joining <c>owned_characters</c> against content on every pull. The alternative —
    /// re-scanning every owned character and resolving each one's <c>LineId</c> from the content
    /// pack — is <c>O(charactersOwned)</c> per pull and grows with account age; this is a single
    /// indexed lookup. The cost is one more row to keep in sync, paid only at the two write sites
    /// that create a first copy.
    /// </para>
    /// <para>
    /// A row here is never deleted (even if every copy of the line is later consumed as evolve
    /// fodder) — "has this account ever owned it" is what determines duplicate vs. new, not
    /// "currently owns it", matching how docs/12 describes Echo shards as tracking lifetime
    /// duplicates, not current inventory.
    /// </para>
    /// </summary>
    public sealed class AccountLineEntity
    {
        public string AccountId { get; set; }

        public string LineId { get; set; }

        public DateTimeOffset FirstObtainedAt { get; set; }
    }
}
