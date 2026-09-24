using System;

namespace DarkMyst.Api.Data.Entities
{
    public enum LedgerKind
    {
        Gold = 0,
        Material = 1,
        Character = 2,

        /// <summary>Premium currency (<see cref="AccountEntity.Gems"/>) — added with the summon
        /// system, see that property's remarks.</summary>
        Gems = 3
    }

    /// <summary>
    /// One append-only record of a currency/item/character movement. Never updated, never
    /// deleted. This is what makes docs/07-testing-plan.md's "ตรวจย้อนหลังได้" (auditable after
    /// the fact) true: summing every row for an account reproduces its current inventory exactly,
    /// for every account, after any sequence of operations — see
    /// <c>LedgerReconciliationTests</c>.
    /// </summary>
    public sealed class LedgerEntryEntity
    {
        public long Id { get; set; }

        public string AccountId { get; set; }

        public LedgerKind Kind { get; set; }

        /// <summary>Material id for <see cref="LedgerKind.Material"/>, character instance id for
        /// <see cref="LedgerKind.Character"/>, null for <see cref="LedgerKind.Gold"/>.</summary>
        public string RefId { get; set; }

        /// <summary>Positive for a grant, negative for a spend/consume. Never zero.</summary>
        public int Delta { get; set; }

        /// <summary>Short machine-readable cause, e.g. "evolve:consume-fodder", "expedition:clear-reward".</summary>
        public string Reason { get; set; }

        /// <summary>The idempotency key that produced this row, if any — lets an auditor tie a
        /// ledger line back to the exact client request that caused it.</summary>
        public string IdempotencyKey { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
