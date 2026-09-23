using System;
using System.Collections.Generic;
using System.IO;
using DarkMyst.Content;
using DarkMyst.Sim;
using Xunit;

namespace DarkMyst.Sim.Tests
{
    /// <summary>
    /// Exercises <c>simrunner summon</c>'s model (docs/12-summon-spec.md §"การันตีสามชั้น",
    /// §"ตัวซ้ำต้องไม่มีวันเป็นของเหลือ", §"วิธีวัด") against the real, shipped <c>content/</c>
    /// directory — the same pack <c>BattleSweepRunnerTests</c> uses — so these are the real
    /// numbers content 0.4.0 produces, not numbers from a hand-built fixture.
    /// </summary>
    public sealed class SummonSimulatorTests
    {
        private static readonly Lazy<ContentPack> Pack = new Lazy<ContentPack>(
            () => ContentPack.LoadFromDirectory(ContentDirectory));

        private static string ContentDirectory => Path.Combine(FindRepositoryRoot(), "content");

        // ------------------------------------------------------------------
        // SummonRules — pure math, no content pack needed
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(39, 400)]
        [InlineData(40, 650)]
        [InlineData(59, 5400)]
        [InlineData(60, 10000)]
        public void ComputeR4PlusChanceBasisPoints_matches_the_spec_at_the_documented_breakpoints(
            int pullsSinceLastR4Plus, int expectedBasisPoints)
        {
            Assert.Equal(expectedBasisPoints, SummonRules.Proposed.ComputeR4PlusChanceBasisPoints(pullsSinceLastR4Plus));
        }

        [Fact]
        public void ComputeR4PlusChanceBasisPoints_is_flat_at_the_base_rate_before_soft_pity_starts()
        {
            Assert.Equal(400, SummonRules.Proposed.ComputeR4PlusChanceBasisPoints(1));
            Assert.Equal(400, SummonRules.Proposed.ComputeR4PlusChanceBasisPoints(20));
        }

        [Fact]
        public void ComputeR4PlusChanceBasisPoints_never_exceeds_10000_past_hard_pity()
        {
            Assert.Equal(10000, SummonRules.Proposed.ComputeR4PlusChanceBasisPoints(61));
            Assert.Equal(10000, SummonRules.Proposed.ComputeR4PlusChanceBasisPoints(1000));
        }

        [Fact]
        public void Base_tier_rates_that_do_not_sum_to_10000_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => new SummonRules(
                r5RateBasisPoints: 100, r4RateBasisPoints: 300, r3RateBasisPoints: 3600, r2RateBasisPoints: 5999,
                floorWindowPulls: 9, softPityStartPull: 40, softPityBasisPointsPerStep: 250, hardPityPull: 60,
                sparkThreshold: 150, duplicateShardsR2: 5, duplicateShardsR3: 10, duplicateShardsR4: 20,
                duplicateShardsR5: 40, shardsPerPerMille: 4, defaultAttuneCapPerMille: 300));
        }

        [Fact]
        public void Attune_arithmetic_reaches_the_cap_at_exactly_1200_shards_and_clamps_there()
        {
            // 4 shards = 1‰, cap 300‰ => 1200 shards caps a line. This is the same arithmetic
            // SummonSimulator applies per duplicate pull; asserted directly here so the constant
            // (1200) is provable independent of a full sweep.
            SummonRules rules = SummonRules.Proposed;
            Assert.Equal(300, 1200 / rules.ShardsPerPerMille);
            Assert.Equal(300, Math.Min(rules.DefaultAttuneCapPerMille, 1600 / rules.ShardsPerPerMille));
            Assert.Equal(299, 1199 / rules.ShardsPerPerMille);
        }

        // ------------------------------------------------------------------
        // SummonSimulator — full sweeps against real content
        // ------------------------------------------------------------------

        [Fact]
        public void Run_never_exceeds_the_pity_guarantees_over_a_large_sweep()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 200,
                Pulls = 400,
                AttuneMaxPulls = 400,
                Seed = 20260923
            });

            Assert.True(
                report.MaxObservedPullsWithoutR4Plus <= 60,
                "observed a gap of " + report.MaxObservedPullsWithoutR4Plus + " pulls without R4+, hard pity is 60.");
            Assert.True(
                report.MaxObservedPullsWithoutR3Plus <= 10,
                "observed a gap of " + report.MaxObservedPullsWithoutR3Plus + " pulls without R3+, the floor is 10.");
        }

        [Fact]
        public void Run_is_deterministic_for_the_same_seed()
        {
            var request = new SummonRequest { Players = 50, Pulls = 200, AttuneMaxPulls = 200, Seed = 42 };

            SummonReport a = SummonSimulator.Run(Pack.Value, request);
            SummonReport b = SummonSimulator.Run(Pack.Value, request);

            Assert.Equal(a.FirstR4Plus.MeanTimes10, b.FirstR4Plus.MeanTimes10);
            Assert.Equal(a.FirstR4Plus.ReachedCount, b.FirstR4Plus.ReachedCount);
            Assert.Equal(a.FirstR5.ReachedCount, b.FirstR5.ReachedCount);
            Assert.Equal(a.MaxObservedPullsWithoutR4Plus, b.MaxObservedPullsWithoutR4Plus);
            Assert.Equal(a.DuplicatesPerLine.Count, b.DuplicatesPerLine.Count);
            for (int i = 0; i < a.DuplicatesPerLine.Count; i++)
            {
                Assert.Equal(a.DuplicatesPerLine[i].LineId, b.DuplicatesPerLine[i].LineId);
                Assert.Equal(a.DuplicatesPerLine[i].TotalPulls, b.DuplicatesPerLine[i].TotalPulls);
                Assert.Equal(a.DuplicatesPerLine[i].Duplicates, b.DuplicatesPerLine[i].Duplicates);
            }
        }

        [Fact]
        public void Run_produces_different_results_for_a_different_seed()
        {
            SummonReport a = SummonSimulator.Run(Pack.Value, new SummonRequest { Players = 50, Pulls = 200, AttuneMaxPulls = 200, Seed = 1 });
            SummonReport b = SummonSimulator.Run(Pack.Value, new SummonRequest { Players = 50, Pulls = 200, AttuneMaxPulls = 200, Seed = 2 });

            Assert.NotEqual(a.FirstR4Plus.MeanTimes10, b.FirstR4Plus.MeanTimes10);
        }

        [Fact]
        public void Run_never_produces_an_R5_result_without_a_hypothetical_line()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 300,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 7,
                HypotheticalR5Count = 0
            });

            Assert.NotEmpty(report.EmptyTierWarnings);
            Assert.Contains(report.EmptyTierWarnings, w => w.StartsWith("R5", StringComparison.Ordinal));

            TierDuplicateStat r5 = report.DuplicatesPerTier.Find(t => t.Rarity == 5);
            Assert.True(r5 == null || r5.TotalPulls == 0);
            Assert.DoesNotContain(report.PerLineFirstPull, l => l.Rarity == 5);
        }

        [Fact]
        public void Run_produces_R5_results_once_a_hypothetical_line_is_added()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 2000,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 7,
                HypotheticalR5Count = 1
            });

            Assert.DoesNotContain(report.EmptyTierWarnings, w => w.StartsWith("R5", StringComparison.Ordinal));

            TierDuplicateStat r5 = report.DuplicatesPerTier.Find(t => t.Rarity == 5);
            Assert.NotNull(r5);
            Assert.True(r5.TotalPulls > 0, "expected at least one R5 pull across 2000 players × 300 pulls.");
            Assert.Contains(report.PerLineFirstPull, l => l.Rarity == 5 && l.IsHypothetical);
        }

        [Fact]
        public void Run_observed_R4Plus_rate_is_close_to_the_base_rate_with_pity_disabled()
        {
            SummonRules noPity = SummonRules.Proposed.WithPityDisabled();
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 500,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 99,
                Rules = noPity
            });

            long totalPulls = 500L * 300;
            long r4PlusPulls = 0;
            foreach (TierDuplicateStat tier in report.DuplicatesPerTier)
            {
                if (tier.Rarity >= 4)
                {
                    r4PlusPulls += tier.TotalPulls;
                }
            }

            // Base rate is 400bp = 4%. No pity means no boost toward it; allow a wide statistical
            // band (3.5%-4.5%) rather than pin an exact float, since this is a random sweep.
            double observedPercent = r4PlusPulls * 100.0 / totalPulls;
            Assert.InRange(observedPercent, 3.5, 4.5);
        }

        [Fact]
        public void Run_rejects_a_non_positive_player_count()
        {
            Assert.Throws<ArgumentException>(() => SummonSimulator.Run(Pack.Value, new SummonRequest { Players = 0 }));
        }

        [Fact]
        public void Run_rejects_a_negative_hypothetical_r5_count()
        {
            Assert.Throws<ArgumentException>(() => SummonSimulator.Run(
                Pack.Value, new SummonRequest { Players = 10, HypotheticalR5Count = -1 }));
        }

        [Fact]
        public void Run_reports_duplicate_rates_and_attune_progress_for_real_lines()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 300,
                Pulls = 300,
                AttuneMaxPulls = 3000,
                Seed = 123
            });

            Assert.NotEmpty(report.DuplicatesPerLine);
            foreach (LineDuplicateStat line in report.DuplicatesPerLine)
            {
                Assert.True(line.Duplicates <= line.TotalPulls);
                Assert.InRange(line.DuplicateRateBasisPoints, 0, 10000);
            }

            Assert.NotNull(report.FirstAttuneCapAnyLine);
            Assert.InRange(report.FirstAttuneCapAnyLine.ReachedCount, 0, 300);
        }

        // ------------------------------------------------------------------

        private static string FindRepositoryRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "content")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }

            if (dir == null)
            {
                throw new InvalidOperationException("Could not find repository root (a 'content' directory) above " + AppContext.BaseDirectory);
            }

            return dir;
        }
    }
}
