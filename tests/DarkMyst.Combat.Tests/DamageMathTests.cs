using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Combat.Tests
{
    public class DamageMathTests
    {
        private static readonly CombatRules Rules = CombatRules.Default;

        [Fact]
        public void Mitigation_matching_the_constant_halves_damage()
        {
            // K = 300, so 300 defence gives 300/(300+300) = 50%.
            int damage = DamageMath.Compute(
                attackStat: 1000, mitigationStat: Rules.MitigationConstant, powerPerMille: 1000,
                rowPerMille: 1000, affinityPerMille: 1000, critPerMille: 1000, variancePerMille: 1000,
                rules: Rules);

            Assert.Equal(500, damage);
        }

        [Fact]
        public void Zero_mitigation_passes_the_full_scaled_attack()
        {
            int damage = DamageMath.Compute(1000, 0, 1500, 1000, 1000, 1000, 1000, Rules);
            Assert.Equal(1500, damage);
        }

        [Fact]
        public void Damage_is_never_zero()
        {
            int damage = DamageMath.Compute(1, 100000, 1, 1000, 1000, 1000, 1000, Rules);
            Assert.Equal(1, damage);
        }

        [Fact]
        public void Defence_has_diminishing_returns_but_never_reaches_immunity()
        {
            int at300 = DamageMath.Compute(1000, 300, 1000, 1000, 1000, 1000, 1000, Rules);
            int at600 = DamageMath.Compute(1000, 600, 1000, 1000, 1000, 1000, 1000, Rules);
            int at1200 = DamageMath.Compute(1000, 1200, 1000, 1000, 1000, 1000, 1000, Rules);

            Assert.Equal(500, at300);
            Assert.Equal(333, at600);
            Assert.Equal(200, at1200);
            Assert.True(at1200 > 0);
        }

        [Theory]
        [InlineData(Affinity.Ember, Affinity.Verdant, 1250)]
        [InlineData(Affinity.Verdant, Affinity.Tide, 1250)]
        [InlineData(Affinity.Tide, Affinity.Ember, 1250)]
        [InlineData(Affinity.Verdant, Affinity.Ember, 800)]
        [InlineData(Affinity.Radiant, Affinity.Umbral, 1250)]
        [InlineData(Affinity.Umbral, Affinity.Radiant, 1250)]
        [InlineData(Affinity.Ember, Affinity.Ember, 1000)]
        [InlineData(Affinity.Ember, Affinity.Radiant, 1000)]
        [InlineData(Affinity.Neutral, Affinity.Ember, 1000)]
        [InlineData(Affinity.Ember, Affinity.Neutral, 1000)]
        public void Affinity_chart_is_a_triangle_plus_a_mutual_pair(Affinity attacker, Affinity defender, int expected)
        {
            Assert.Equal(expected, DamageMath.AffinityMultiplierPerMille(attacker, defender, Rules));
        }

        [Fact]
        public void Only_physical_damage_cares_about_the_row()
        {
            Assert.Equal(1100, DamageMath.RowMultiplierPerMille(DamageKind.Physical, Row.Front, Rules));
            Assert.Equal(900, DamageMath.RowMultiplierPerMille(DamageKind.Physical, Row.Back, Rules));
            Assert.Equal(1000, DamageMath.RowMultiplierPerMille(DamageKind.Magical, Row.Front, Rules));
            Assert.Equal(1000, DamageMath.RowMultiplierPerMille(DamageKind.True, Row.Back, Rules));
        }

        [Fact]
        public void Crit_multiplier_adds_the_units_crit_damage()
        {
            Assert.Equal(1500, DamageMath.CritMultiplierPerMille(0, Rules));
            Assert.Equal(1750, DamageMath.CritMultiplierPerMille(250, Rules));
        }

        [Fact]
        public void Stat_modifiers_are_clamped_at_both_ends()
        {
            // The ceiling stops a stack of buffs turning into an unreadable number...
            Assert.Equal(2500, DamageMath.ApplyStatModifier(1000, 5000, Rules));

            // ...and the floor stops debuff stacking from zeroing a stat outright.
            Assert.Equal(250, DamageMath.ApplyStatModifier(1000, -5000, Rules));
        }

        [Fact]
        public void The_pipeline_multiplies_in_a_fixed_order()
        {
            // 1000 atk * 1.2 power = 1200; mitigation 300/600 = 600; row 1.1 = 660;
            // affinity 1.25 = 825; crit 1.5 = 1237; variance 1.05 = 1298.
            int damage = DamageMath.Compute(1000, 300, 1200, 1100, 1250, 1500, 1050, Rules);
            Assert.Equal(1298, damage);
        }

        [Fact]
        public void Large_inputs_do_not_overflow()
        {
            int damage = DamageMath.Compute(
                int.MaxValue, 0, 10000, 1100, 1250, 1750, 1050, Rules);

            Assert.Equal(int.MaxValue, damage);
        }
    }
}
