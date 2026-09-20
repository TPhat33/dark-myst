using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Combat.Tests
{
    /// <summary>
    /// The whole point of the engine: the same request always gives the same answer. Without
    /// this the server cannot verify a client's battle, and a replay cannot be trusted.
    /// </summary>
    public class DeterminismTests
    {
        private static BattleRequest BuildBattle(ulong seed)
        {
            var attacker = TestUnits.Team(
                "attackers",
                TestUnits.Unit("a0", 0, TestUnits.Stats(maxHp: 1400, attack: 130, speed: 96, critRate: 200),
                    new[] { TestUnits.BasicAttack() }, Affinity.Ember),
                TestUnits.Unit("a1", 1, TestUnits.Stats(maxHp: 1200, attack: 120, speed: 104, critRate: 150),
                    new[] { TestUnits.BasicAttack() }, Affinity.Verdant),
                TestUnits.Unit("a3", 3, TestUnits.Stats(maxHp: 900, attack: 150, speed: 130, critRate: 300),
                    new[] { TestUnits.BasicAttack() }, Affinity.Umbral));

            var defender = TestUnits.Team(
                "defenders",
                TestUnits.Unit("d0", 0, TestUnits.Stats(maxHp: 1300, attack: 118, speed: 100),
                    new[] { TestUnits.BasicAttack() }, Affinity.Tide),
                TestUnits.Unit("d1", 1, TestUnits.Stats(maxHp: 1300, attack: 118, speed: 100),
                    new[] { TestUnits.BasicAttack() }, Affinity.Radiant),
                TestUnits.Unit("d4", 4, TestUnits.Stats(maxHp: 800, attack: 160, speed: 126, critRate: 250),
                    new[] { TestUnits.BasicAttack() }, Affinity.Ember));

            return TestUnits.Battle(attacker, defender, seed);
        }

        [Fact]
        public void The_same_request_replays_identically()
        {
            BattleResult first = BattleSimulator.Run(BuildBattle(20260920));
            BattleResult second = BattleSimulator.Run(BuildBattle(20260920));

            Assert.Equal(first.Checksum, second.Checksum);
            Assert.Equal(first.RngCalls, second.RngCalls);
            Assert.Equal(first.Outcome, second.Outcome);
            Assert.Equal(first.Events.Count, second.Events.Count);

            for (int i = 0; i < first.Events.Count; i++)
            {
                Assert.Equal(first.Events[i].ToCanonicalString(), second.Events[i].ToCanonicalString());
            }
        }

        [Fact]
        public void A_different_seed_produces_a_different_battle()
        {
            var checksums = new HashSet<string>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                checksums.Add(BattleSimulator.Run(BuildBattle(seed)).Checksum);
            }

            // Twelve seeds that all collapse to the same log would mean the RNG is not
            // reaching the simulation at all.
            Assert.True(checksums.Count > 1, "Seeds had no effect on the outcome.");
        }

        [Fact]
        public void Random_streams_are_reproducible_and_unbiased_enough_to_trust()
        {
            var first = new DeterministicRandom(42);
            var second = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(first.NextUInt32(), second.NextUInt32());
            }

            var different = new DeterministicRandom(43);
            Assert.NotEqual(new DeterministicRandom(42).NextUInt32(), different.NextUInt32());
        }

        [Fact]
        public void Bounded_draws_stay_inside_their_range_and_cover_it()
        {
            var rng = new DeterministicRandom(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++)
            {
                int value = rng.NextInt(10, 15);
                Assert.InRange(value, 10, 14);
                seen.Add(value);
            }

            Assert.Equal(5, seen.Count);
        }

        [Fact]
        public void Chance_rolls_land_near_their_stated_rate()
        {
            var rng = new DeterministicRandom(11);
            int hits = 0;
            for (int i = 0; i < 20000; i++)
            {
                if (rng.Chance(250))
                {
                    hits++;
                }
            }

            // 25% of 20000 is 5000; allow a generous band so this never flakes.
            Assert.InRange(hits, 4600, 5400);
        }

        [Fact]
        public void Call_count_starts_at_zero_and_tracks_every_draw()
        {
            var rng = new DeterministicRandom(5);
            Assert.Equal(0, rng.CallCount);

            rng.NextUInt32();
            rng.NextUInt32();
            Assert.Equal(2, rng.CallCount);
        }

        [Fact]
        public void Certain_and_impossible_chances_do_not_consume_the_stream()
        {
            var rng = new DeterministicRandom(5);
            Assert.True(rng.Chance(1000));
            Assert.False(rng.Chance(0));
            Assert.Equal(0, rng.CallCount);
        }
    }
}
