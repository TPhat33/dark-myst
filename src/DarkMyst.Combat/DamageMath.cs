using DarkMyst.Combat.Model;

namespace DarkMyst.Combat
{
    /// <summary>
    /// The damage pipeline, as pure integer functions.
    /// <para>
    /// Kept separate from the simulator so the formula can be unit-tested and balanced on its
    /// own, and so a spreadsheet can reproduce it exactly. The order of the multiplications is
    /// part of the contract: integer division truncates, so reordering them changes results.
    /// </para>
    /// </summary>
    public static class DamageMath
    {
        /// <summary>Returns the attacker's affinity multiplier against the defender, in per-mille.</summary>
        public static int AffinityMultiplierPerMille(Affinity attacker, Affinity defender, CombatRules rules)
        {
            if (attacker == Affinity.Neutral || defender == Affinity.Neutral || attacker == defender)
            {
                return rules.AffinityNeutralPerMille;
            }

            if (IsStrongAgainst(attacker, defender))
            {
                return rules.AffinityAdvantagePerMille;
            }

            if (IsStrongAgainst(defender, attacker))
            {
                return rules.AffinityDisadvantagePerMille;
            }

            return rules.AffinityNeutralPerMille;
        }

        private static bool IsStrongAgainst(Affinity a, Affinity b)
        {
            switch (a)
            {
                case Affinity.Ember: return b == Affinity.Verdant;
                case Affinity.Verdant: return b == Affinity.Tide;
                case Affinity.Tide: return b == Affinity.Ember;
                case Affinity.Radiant: return b == Affinity.Umbral;
                case Affinity.Umbral: return b == Affinity.Radiant;
                default: return false;
            }
        }

        /// <summary>
        /// Row modifier applied to the defender, in per-mille. Only physical damage cares where
        /// a unit stands; spells and true damage reach the back line unchanged.
        /// </summary>
        public static int RowMultiplierPerMille(DamageKind kind, Row defenderRow, CombatRules rules)
        {
            if (kind != DamageKind.Physical)
            {
                return 1000;
            }

            return defenderRow == Row.Front
                ? rules.FrontRowPhysicalTakenPerMille
                : rules.BackRowPhysicalTakenPerMille;
        }

        /// <summary>Which stat a damage kind scales off.</summary>
        public static Stat DefaultScalingStat(DamageKind kind)
        {
            return kind == DamageKind.Magical ? Stat.Magic : Stat.Attack;
        }

        /// <summary>Which stat mitigates a damage kind.</summary>
        public static Stat DefaultMitigationStat(DamageKind kind)
        {
            return kind == DamageKind.Magical ? Stat.Resist : Stat.Defense;
        }

        /// <summary>
        /// Crit multiplier in per-mille: the base multiplier plus the attacker's crit damage bonus.
        /// </summary>
        public static int CritMultiplierPerMille(int critDamagePerMille, CombatRules rules)
        {
            int value = rules.BaseCritMultiplierPerMille + critDamagePerMille;
            return value < 1000 ? 1000 : value;
        }

        /// <summary>
        /// The one place damage is computed.
        /// <code>
        /// raw        = attack * power / 1000
        /// mitigated  = raw * K / (defense + K)
        /// result     = mitigated * row / 1000 * affinity / 1000 * crit / 1000 * variance / 1000
        /// </code>
        /// Always at least 1, so a heavily outscaled attacker still chips away.
        /// <para>
        /// <c>attackStat</c> is the attacker's scaling stat after modifiers, and
        /// <c>mitigationStat</c> the defender's mitigating stat after modifiers — pass 0 for
        /// true damage, which skips mitigation entirely.
        /// </para>
        /// </summary>
        public static int Compute(
            int attackStat,
            int mitigationStat,
            int powerPerMille,
            int rowPerMille,
            int affinityPerMille,
            int critPerMille,
            int variancePerMille,
            CombatRules rules)
        {
            if (attackStat < 0)
            {
                attackStat = 0;
            }

            if (mitigationStat < 0)
            {
                mitigationStat = 0;
            }

            long value = (long)attackStat * powerPerMille / 1000L;
            value = value * rules.MitigationConstant / (mitigationStat + rules.MitigationConstant);
            value = value * rowPerMille / 1000L;
            value = value * affinityPerMille / 1000L;
            value = value * critPerMille / 1000L;
            value = value * variancePerMille / 1000L;

            if (value < 1L)
            {
                return 1;
            }

            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        /// <summary>
        /// Applies a summed per-mille modifier to a base stat, clamping the modifier first.
        /// Never returns a negative stat.
        /// </summary>
        public static int ApplyStatModifier(int baseValue, int totalPerMille, CombatRules rules)
        {
            if (totalPerMille < rules.StatModifierFloorPerMille)
            {
                totalPerMille = rules.StatModifierFloorPerMille;
            }
            else if (totalPerMille > rules.StatModifierCeilingPerMille)
            {
                totalPerMille = rules.StatModifierCeilingPerMille;
            }

            long value = (long)baseValue * (1000L + totalPerMille) / 1000L;
            if (value < 0L)
            {
                return 0;
            }

            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
