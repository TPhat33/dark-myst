using System;
using System.Collections.Generic;

namespace DarkMyst.Combat.Model
{
    public enum BattleEventKind
    {
        BattleStarted = 0,
        RoundStarted = 1,
        TurnStarted = 2,

        /// <summary>The unit lost its turn, e.g. to a stun. <c>Note</c> says why.</summary>
        TurnSkipped = 3,

        SkillActivated = 4,
        Damaged = 5,
        Healed = 6,
        ShieldAbsorbed = 7,
        StatusApplied = 8,
        StatusRefreshed = 9,

        /// <summary>The application chance failed. Useful when a player asks why nothing happened.</summary>
        StatusResisted = 10,

        StatusTicked = 11,
        StatusExpired = 12,
        StatusRemoved = 13,
        UnitDowned = 14,
        UnitRevived = 15,
        TurnEnded = 16,
        RoundEnded = 17,
        BattleEnded = 18
    }

    /// <summary>
    /// A stable address for a unit. Slots never move during a battle, so this stays valid for
    /// the whole log — including after the unit goes down.
    /// </summary>
    public struct UnitRef : IEquatable<UnitRef>
    {
        public TeamSide Side;
        public int Slot;

        public UnitRef(TeamSide side, int slot)
        {
            Side = side;
            Slot = slot;
        }

        public bool Equals(UnitRef other) => Side == other.Side && Slot == other.Slot;

        public override bool Equals(object obj) => obj is UnitRef other && Equals(other);

        public override int GetHashCode() => ((int)Side * 397) ^ Slot;

        public override string ToString() => (Side == TeamSide.Attacker ? "A" : "D") + Slot;
    }

    /// <summary>
    /// One entry in the battle log. Deliberately a single flat type rather than a class
    /// hierarchy: Unity replays it, the admin tool renders it, and both serialize it as-is.
    /// <para>
    /// Unused fields stay at their defaults for a given kind. See
    /// <c>docs/02-combat-spec.md</c> for which fields each kind fills in.
    /// </para>
    /// </summary>
    public sealed class BattleEvent
    {
        public BattleEventKind Kind { get; set; }

        /// <summary>Monotonic index within the battle, starting at 0.</summary>
        public int Sequence { get; set; }

        /// <summary>1-based round. 0 for events outside any round.</summary>
        public int Round { get; set; }

        public UnitRef? Source { get; set; }

        public UnitRef? Target { get; set; }

        public string SkillId { get; set; }

        public string StatusId { get; set; }

        /// <summary>Damage, healing, absorbed amount, stack count — depends on the kind.</summary>
        public int Amount { get; set; }

        public bool Critical { get; set; }

        /// <summary>Target HP after the event. -1 when the event has no HP effect.</summary>
        public int TargetHpAfter { get; set; } = -1;

        /// <summary>Free-form detail for the log reader. Never parsed by game logic.</summary>
        public string Note { get; set; }

        /// <summary>
        /// Canonical text form used for the battle checksum. Must include every field that can
        /// differ between two runs, and nothing that cannot.
        /// </summary>
        public string ToCanonicalString()
        {
            return string.Join("|", new[]
            {
                ((int)Kind).ToString(),
                Round.ToString(),
                Source.HasValue ? Source.Value.ToString() : "-",
                Target.HasValue ? Target.Value.ToString() : "-",
                SkillId ?? "-",
                StatusId ?? "-",
                Amount.ToString(),
                Critical ? "1" : "0",
                TargetHpAfter.ToString()
            });
        }

        public override string ToString()
        {
            var parts = new List<string> { "[" + Round + "] " + Kind };
            if (Source.HasValue)
            {
                parts.Add("src=" + Source.Value);
            }

            if (Target.HasValue)
            {
                parts.Add("tgt=" + Target.Value);
            }

            if (!string.IsNullOrEmpty(SkillId))
            {
                parts.Add("skill=" + SkillId);
            }

            if (!string.IsNullOrEmpty(StatusId))
            {
                parts.Add("status=" + StatusId);
            }

            if (Amount != 0)
            {
                parts.Add("amount=" + Amount);
            }

            if (Critical)
            {
                parts.Add("CRIT");
            }

            if (TargetHpAfter >= 0)
            {
                parts.Add("hp=" + TargetHpAfter);
            }

            if (!string.IsNullOrEmpty(Note))
            {
                parts.Add("(" + Note + ")");
            }

            return string.Join(" ", parts);
        }
    }
}
