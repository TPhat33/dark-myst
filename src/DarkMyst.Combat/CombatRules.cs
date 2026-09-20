using DarkMyst.Combat.Model;

namespace DarkMyst.Combat
{
    /// <summary>
    /// Every tunable number the simulator reads. Nothing else in the engine hard-codes a
    /// balance value.
    /// <para>
    /// Changing any field here changes the outcome of already-recorded battles, so
    /// <see cref="Version"/> must be bumped and stored alongside every battle log and every
    /// owned character. See <c>docs/02-combat-spec.md</c>.
    /// </para>
    /// </summary>
    public sealed class CombatRules
    {
        /// <summary>Semantic version of the rule set. Bump on any behavioural change.</summary>
        public const string Version = "1.0.0";

        /// <summary>
        /// Mitigation constant K in <c>K / (defense + K)</c>. Higher K means defence matters less.
        /// At K = 300, 300 defence halves incoming damage.
        /// </summary>
        public int MitigationConstant { get; set; } = 300;

        /// <summary>Hard cap on rounds. Reaching it resolves the battle on remaining HP share.</summary>
        public int MaxRounds { get; set; } = 30;

        /// <summary>Physical damage taken by a front-row unit, in per-mille.</summary>
        public int FrontRowPhysicalTakenPerMille { get; set; } = 1100;

        /// <summary>Physical damage taken by a back-row unit, in per-mille.</summary>
        public int BackRowPhysicalTakenPerMille { get; set; } = 900;

        public int AffinityAdvantagePerMille { get; set; } = 1250;

        public int AffinityDisadvantagePerMille { get; set; } = 800;

        public int AffinityNeutralPerMille { get; set; } = 1000;

        /// <summary>Crit multiplier before the unit's own CritDamage is added, in per-mille.</summary>
        public int BaseCritMultiplierPerMille { get; set; } = 1500;

        public int VarianceMinPerMille { get; set; } = 950;

        /// <summary>Inclusive upper bound of the damage variance roll.</summary>
        public int VarianceMaxPerMille { get; set; } = 1050;

        /// <summary>Lower clamp on the summed stat modifiers applied to one stat, in per-mille.</summary>
        public int StatModifierFloorPerMille { get; set; } = -750;

        /// <summary>Upper clamp on the summed stat modifiers applied to one stat, in per-mille.</summary>
        public int StatModifierCeilingPerMille { get; set; } = 1500;

        /// <summary>
        /// How deep reactions may nest. A reaction fired at this depth cannot fire further
        /// reactions, which is what stops two "counter-attack on hit" units looping forever.
        /// </summary>
        public int MaxReactionDepth { get; set; } = 2;

        /// <summary>
        /// Turns of stun immunity granted after a unit loses a turn to a stun. It is spent by
        /// taking a turn, not by the clock, so at 1 a unit can be stunned at most every other
        /// turn. Without it a pair of stunners locks a boss out of the whole battle.
        /// </summary>
        public int StunImmuneRoundsAfterStun { get; set; } = 1;

        public static CombatRules Default => new CombatRules();

        public CombatRules Clone()
        {
            return new CombatRules
            {
                MitigationConstant = MitigationConstant,
                MaxRounds = MaxRounds,
                FrontRowPhysicalTakenPerMille = FrontRowPhysicalTakenPerMille,
                BackRowPhysicalTakenPerMille = BackRowPhysicalTakenPerMille,
                AffinityAdvantagePerMille = AffinityAdvantagePerMille,
                AffinityDisadvantagePerMille = AffinityDisadvantagePerMille,
                AffinityNeutralPerMille = AffinityNeutralPerMille,
                BaseCritMultiplierPerMille = BaseCritMultiplierPerMille,
                VarianceMinPerMille = VarianceMinPerMille,
                VarianceMaxPerMille = VarianceMaxPerMille,
                StatModifierFloorPerMille = StatModifierFloorPerMille,
                StatModifierCeilingPerMille = StatModifierCeilingPerMille,
                MaxReactionDepth = MaxReactionDepth,
                StunImmuneRoundsAfterStun = StunImmuneRoundsAfterStun
            };
        }
    }

    /// <summary>Slot layout shared by both teams.</summary>
    public static class Formation
    {
        public const int SlotCount = 5;
        public const int FrontRowSlots = 2;

        public static Row RowOf(int slot) => slot < FrontRowSlots ? Row.Front : Row.Back;
    }
}
