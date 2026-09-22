using System;
using System.Collections.Generic;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Content.Tests
{
    /// <summary>
    /// Enforces the roster composition shape that <c>simrunner roster</c> reports (see
    /// <c>tools/DarkMyst.SimRunner/Program.cs</c> and docs/07-testing-plan.md). Two real content
    /// bugs shipped before anything checked this: enemy affinities were lopsided enough that one
    /// affinity was effectively free of risk everywhere, and the only two rarity-4 lines a player
    /// could pull both filled the single most decisive role in the roster (a team with no healer
    /// measured 0% — see docs/07-testing-plan.md). A hand-maintained note about "keep the roster
    /// balanced" goes stale the moment someone adds a character without reading it; these tests
    /// turn red the moment content does, instead of waiting for the next balance sweep to notice.
    /// <para>
    /// The line/rarity axes are scoped to stage-I characters throughout: a stage II/III form is
    /// only ever reached by evolving an owned character, never pulled or rewarded directly (every
    /// <c>Character</c> reward entry in <c>stages.json</c> names a stage-I id), so it plays no
    /// part in what a summon or reward can hand a player and would only dilute these counts.
    /// </para>
    /// </summary>
    public class RosterShapeTests
    {
        /// <summary>
        /// With 6 affinities and no other constraint, an even split would be ~16.7% each. 40% is
        /// roughly 2.4x that — enough headroom that an author can lean an encounter or two toward
        /// one affinity on purpose (the warlock conclave is deliberately Umbral-heavy, to give the
        /// Radiant/Umbral mirror a real matchup) without the whole enemy roster quietly becoming
        /// "one affinity always wins for free", which is the actual bug this test exists to catch.
        /// </summary>
        private const double MaxAffinitySharePercent = 40.0;

        [Fact]
        public void No_enemy_affinity_makes_up_more_than_forty_percent_of_all_encounter_units()
        {
            ContentPack pack = ShippedContent.Instance;

            var counts = new Dictionary<Affinity, int>();
            int total = 0;
            foreach (EncounterData encounter in pack.Encounters)
            {
                foreach (EncounterUnitData unit in encounter.Units)
                {
                    Affinity affinity = pack.GetEnemy(unit.EnemyId).Affinity;
                    counts.TryGetValue(affinity, out int soFar);
                    counts[affinity] = soFar + 1;
                    total++;
                }
            }

            Assert.True(total > 0, "No encounter has any units at all — nothing to check.");

            foreach (KeyValuePair<Affinity, int> entry in counts)
            {
                double share = entry.Value * 100.0 / total;
                Assert.True(
                    share <= MaxAffinitySharePercent,
                    entry.Key + " is " + share.ToString("0.0") + "% of all enemy units ("
                    + entry.Value + "/" + total + "), over the " + MaxAffinitySharePercent
                    + "% ceiling. An affinity this dominant on the enemy side is a free ride for "
                    + "whatever beats it and a dead matchup for everyone else — spread it out.");
            }
        }

        [Fact]
        public void Every_affinity_has_at_least_one_enemy_unit_and_one_playable_stage_one_line()
        {
            ContentPack pack = ShippedContent.Instance;

            var enemyAffinities = new HashSet<Affinity>();
            foreach (EncounterData encounter in pack.Encounters)
            {
                foreach (EncounterUnitData unit in encounter.Units)
                {
                    enemyAffinities.Add(pack.GetEnemy(unit.EnemyId).Affinity);
                }
            }

            var playableAffinities = new HashSet<Affinity>();
            foreach (CharacterData character in pack.Characters)
            {
                if (character.EvolveStage == 1)
                {
                    playableAffinities.Add(character.Affinity);
                }
            }

            foreach (Affinity affinity in (Affinity[])Enum.GetValues(typeof(Affinity)))
            {
                // Neutral is exempt on the enemy side only: it deliberately never gains or
                // suffers from the triangle, so an enemy roster with zero Neutral units is a
                // valid design (every fight leans on the triangle), unlike a missing elemental
                // affinity, which is a hole nothing can ever be advantaged or disadvantaged
                // against. It still must have a playable line, same as every other affinity.
                if (affinity != Affinity.Neutral)
                {
                    Assert.True(
                        enemyAffinities.Contains(affinity),
                        affinity + " has no enemy unit in any encounter — nothing in the game "
                        + "lets a character of this affinity fight with (or against) the "
                        + "triangle advantage it should have.");
                }

                Assert.True(
                    playableAffinities.Contains(affinity),
                    affinity + " has no stage-I character line — a player can never pull or "
                    + "reward their way into this affinity.");
            }
        }

        [Fact]
        public void The_top_pullable_rarity_tier_is_not_concentrated_in_a_single_role()
        {
            ContentPack pack = ShippedContent.Instance;

            int topRarity = 0;
            var rolesAtTopRarity = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CharacterData character in pack.Characters)
            {
                if (character.EvolveStage != 1)
                {
                    continue;
                }

                if (character.Rarity > topRarity)
                {
                    topRarity = character.Rarity;
                    rolesAtTopRarity.Clear();
                }

                if (character.Rarity == topRarity)
                {
                    rolesAtTopRarity.TryGetValue(character.Role, out int soFar);
                    rolesAtTopRarity[character.Role] = soFar + 1;
                }
            }

            int tierTotal = 0;
            int maxInOneRole = 0;
            foreach (int roleCount in rolesAtTopRarity.Values)
            {
                tierTotal += roleCount;
                if (roleCount > maxInOneRole)
                {
                    maxInOneRole = roleCount;
                }
            }

            Assert.True(tierTotal > 0, "No stage-I character has a rarity at all — nothing to check.");

            // A tier of exactly one line has nothing to be "concentrated" against yet; the rule
            // only bites once a second top-rarity line exists and every one of them shares a
            // role — the exact shape that made the top summon tier "always a Mender" before this
            // round's fix (Tide Oracle and Dawn Cantor, both rarity 4, both Mender). Requiring
            // strictly less than 100% (rather than some softer bound) is deliberate: with a
            // roster this small, "not literally every top-tier line is the same role" is already
            // the whole ask — a summon system built on top of this only needs one role escape
            // hatch at the top tier, not a proportional split.
            double concentrationPercent = maxInOneRole * 100.0 / tierTotal;
            Assert.True(
                tierTotal == 1 || concentrationPercent < 100.0,
                "Every rarity-" + topRarity + " stage-I line (the top pullable tier, " + tierTotal
                + " line(s)) shares the same role — a summon system's best tier would always hand "
                + "out the same role. Diversify the top tier's roles, or add a line of a "
                + "different role at that rarity.");
        }
    }
}
