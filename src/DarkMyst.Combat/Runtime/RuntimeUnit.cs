using System.Collections.Generic;
using DarkMyst.Combat.Model;

namespace DarkMyst.Combat.Runtime
{
    /// <summary>A status currently sitting on a unit.</summary>
    public sealed class StatusInstance
    {
        public string Id;
        public EffectKind Kind;
        public Stat ModifiedStat;

        /// <summary>Per-mille magnitude for one stack.</summary>
        public int AmountPerMille;

        /// <summary>
        /// For DoT/HoT: damage or healing per tick per stack, snapshotted from the caster when
        /// the status was applied. Later buffs on the caster do not retroactively change it —
        /// that keeps "why did I take 340 poison damage" answerable from the log.
        /// </summary>
        public int TickAmount;

        public int RemainingRounds;
        public int Stacks = 1;
        public int MaxStacks = 1;
        public StackRule StackRule = StackRule.Refresh;
        public UnitRef Source;

        /// <summary>True for buffs (Dispel removes these), false for debuffs (Cleanse removes these).</summary>
        public bool IsBeneficial;
    }

    /// <summary>An absorb pool. Consumed oldest first.</summary>
    public sealed class ShieldInstance
    {
        public string Id;
        public int Remaining;
        public int RemainingRounds;
        public UnitRef Source;
    }

    /// <summary>Mutable battle state for one unit. Lives only for the duration of a battle.</summary>
    public sealed class RuntimeUnit
    {
        public UnitDefinition Definition;
        public UnitRef Ref;
        public Row Row;
        public bool IsLeader;

        public int Hp;
        public bool Alive = true;

        public readonly List<StatusInstance> Statuses = new List<StatusInstance>();
        public readonly List<ShieldInstance> Shields = new List<ShieldInstance>();
        public readonly Dictionary<string, int> Cooldowns = new Dictionary<string, int>();

        /// <summary>Turns still to be lost to stun.</summary>
        public int StunRounds;

        /// <summary>
        /// Turns during which further stuns are rejected outright. Spent at the end of a turn
        /// the unit actually takes.
        /// </summary>
        public int StunImmuneRounds;

        public int MaxHp => Definition.Stats.MaxHp;

        public bool IsTaunting
        {
            get
            {
                for (int i = 0; i < Statuses.Count; i++)
                {
                    if (Statuses[i].Kind == EffectKind.Taunt)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Base stat with every StatModifier status summed and clamped. MaxHp is deliberately
        /// not modifiable in v1: resizing a health pool mid-battle raises questions about
        /// current HP that are not worth answering yet.
        /// </summary>
        public int EffectiveStat(Stat stat, CombatRules rules)
        {
            int baseValue = Definition.Stats.Get(stat);
            if (stat == Stat.MaxHp)
            {
                return baseValue;
            }

            int total = 0;
            for (int i = 0; i < Statuses.Count; i++)
            {
                StatusInstance status = Statuses[i];
                if (status.Kind == EffectKind.StatModifier && status.ModifiedStat == stat)
                {
                    total += status.AmountPerMille * status.Stacks;
                }
            }

            if (total == 0)
            {
                return baseValue;
            }

            return DamageMath.ApplyStatModifier(baseValue, total, rules);
        }

        public int TotalShield
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Shields.Count; i++)
                {
                    total += Shields[i].Remaining;
                }

                return total;
            }
        }
    }
}
