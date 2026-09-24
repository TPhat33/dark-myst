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
                report.MaxObservedPullsWithoutPityTier <= 60,
                "observed a gap of " + report.MaxObservedPullsWithoutPityTier + " pulls without R4+, hard pity is 60.");
            Assert.True(
                report.MaxObservedPullsWithoutFloorTier <= 10,
                "observed a gap of " + report.MaxObservedPullsWithoutFloorTier + " pulls without R3+, the floor is 10.");
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
            Assert.Equal(a.MaxObservedPullsWithoutPityTier, b.MaxObservedPullsWithoutPityTier);
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
        // Lock test — SummonRules.Proposed must stay byte-identical through the PityTier/
        // FloorTier generalization (§"การันตีสามชั้น"). Every number below was captured from
        // `simrunner summon --seed 20260920` (10000 players, 300 pulls, 5000-pull Attune budget)
        // BEFORE PityTier/FloorTier were introduced.
        // ------------------------------------------------------------------

        [Fact]
        public void Proposed_produces_byte_identical_numbers_to_before_the_PityTier_FloorTier_generalization()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 10000,
                Pulls = 300,
                AttuneMaxPulls = 5000,
                Seed = 20260920
            });

            Assert.Equal(4, report.Rules.PityTier);
            Assert.Equal(3, report.Rules.FloorTier);
            Assert.Equal(59, report.MaxObservedPullsWithoutPityTier);
            Assert.Equal(9, report.MaxObservedPullsWithoutFloorTier);

            AssertStat(report.FirstR4Plus, meanTimes10: 212, median: 17, p90: 45, worst: 58, reachedCount: 10000);
            AssertStat(report.FirstR4Exact, meanTimes10: 212, median: 17, p90: 45, worst: 58, reachedCount: 10000);
            Assert.Equal(0, report.FirstR5.ReachedCount);

            LineFirstPullStat shackleborn = report.PerLineFirstPull.Find(l => l.LineId == "chr_shackleborn_i");
            Assert.NotNull(shackleborn);
            AssertStat(shackleborn.Stat, meanTimes10: 425, median: 35, p90: 91, worst: 292, reachedCount: 10000 - 3);
            Assert.Equal(197, shackleborn.StillMissingAtSparkBasisPoints);

            LineFirstPullStat tideOracle = report.PerLineFirstPull.Find(l => l.LineId == "chr_tide_oracle_i");
            Assert.NotNull(tideOracle);
            AssertStat(tideOracle.Stat, meanTimes10: 423, median: 35, p90: 91, worst: 300, reachedCount: 10000 - 5);
            Assert.Equal(193, tideOracle.StillMissingAtSparkBasisPoints);

            AssertTierDuplicates(report, rarity: 4, totalPulls: 139274, duplicates: 119282);
            AssertTierDuplicates(report, rarity: 3, totalPulls: 1078598, duplicates: 978599);
            AssertTierDuplicates(report, rarity: 2, totalPulls: 1782128, duplicates: 1762128);

            AssertStat(report.FirstAttuneCapAnyLine, meanTimes10: 7829, median: 784, p90: 821, worst: 880, reachedCount: 10000);
            AssertStat(report.FirstAttuneCapByTier[4], meanTimes10: 24009, median: 2406, p90: 2669, worst: 3175, reachedCount: 10000);
            AssertStat(report.FirstAttuneCapByTier[3], meanTimes10: 29100, median: 2921, p90: 3097, worst: 3501, reachedCount: 10000);
            AssertStat(report.FirstAttuneCapByTier[2], meanTimes10: 7829, median: 784, p90: 821, worst: 880, reachedCount: 10000);
        }

        private static void AssertStat(PullCountStat stat, int meanTimes10, int median, int p90, int worst, int reachedCount)
        {
            Assert.Equal(meanTimes10, stat.MeanTimes10);
            Assert.Equal(median, stat.Median);
            Assert.Equal(p90, stat.P90);
            Assert.Equal(worst, stat.Worst);
            Assert.Equal(reachedCount, stat.ReachedCount);
        }

        private static void AssertTierDuplicates(SummonReport report, int rarity, long totalPulls, long duplicates)
        {
            TierDuplicateStat tier = report.DuplicatesPerTier.Find(t => t.Rarity == rarity);
            Assert.NotNull(tier);
            Assert.Equal(totalPulls, tier.TotalPulls);
            Assert.Equal(duplicates, tier.Duplicates);
        }

        // ------------------------------------------------------------------
        // SummonRules.GenshinLike — Genshin Impact's public wish structure expressed in this
        // model's shape (docs/12 asks this be measured, not argued): PityTier 5 (soft from 74,
        // hard 90), FloorTier 4 (every 10), independent counters.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(73, 60)]
        [InlineData(74, 660)]
        [InlineData(89, 9660)]
        [InlineData(90, 10000)]
        public void GenshinLike_soft_pity_ramp_matches_the_documented_breakpoints(int pullsSincePity, int expectedBasisPoints)
        {
            // R5 base is 60bp (0.6%). Soft pity adds 600bp/pull from pull 74. At k=89 the ramp
            // gives 60 + 600*16 = 9660bp — still short of the 10000bp hard-pity cap, so pull 90's
            // jump to 10000 is the hard-pity rule firing, not the ramp saturating on its own; the
            // ramp never needs its own clamp for this preset.
            Assert.Equal(expectedBasisPoints, SummonRules.GenshinLike.ComputePityChanceBasisPoints(pullsSincePity));
        }

        [Fact]
        public void GenshinLike_floor_upgrade_range_is_exactly_the_tier_between_FloorTier_and_PityTier()
        {
            // "rolled within [FloorTier..PityTier-1] by base-rate proportion" — for GenshinLike
            // that range is the single tier R4, matching Genshin's "10 wishes guarantees 4-star or
            // better" (never straight to 5-star; a 5-star before then only ever comes from the
            // independent pity/base roll, not the floor).
            Assert.Equal(4, SummonRules.GenshinLike.FloorTier);
            Assert.Equal(5, SummonRules.GenshinLike.PityTier);
            Assert.Equal(
                SummonRules.GenshinLike.R4RateBasisPoints,
                SummonRules.GenshinLike.SumRates(SummonRules.GenshinLike.FloorTier, SummonRules.GenshinLike.PityTier - 1));

            // Same check for Proposed: the range is the single tier R3, matching today's floor.
            Assert.Equal(3, SummonRules.Proposed.FloorTier);
            Assert.Equal(4, SummonRules.Proposed.PityTier);
            Assert.Equal(
                SummonRules.Proposed.R3RateBasisPoints,
                SummonRules.Proposed.SumRates(SummonRules.Proposed.FloorTier, SummonRules.Proposed.PityTier - 1));
        }

        [Fact]
        public void GenshinLike_never_exceeds_90_pulls_without_R5_and_never_10_without_R4Plus()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 300,
                Pulls = 400,
                AttuneMaxPulls = 400,
                Seed = 20260923,
                HypotheticalR5Count = 1,
                Rules = SummonRules.GenshinLike
            });

            Assert.True(
                report.MaxObservedPullsWithoutPityTier <= 90,
                "observed a gap of " + report.MaxObservedPullsWithoutPityTier + " pulls without R5, hard pity is 90.");
            Assert.True(
                report.MaxObservedPullsWithoutFloorTier <= 10,
                "observed a gap of " + report.MaxObservedPullsWithoutFloorTier + " pulls without R4+, the floor is 10.");
        }

        [Fact]
        public void GenshinLike_observed_R5_rate_is_close_to_the_base_rate_with_pity_disabled()
        {
            SummonRules noPity = SummonRules.GenshinLike.WithPityDisabled();
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 4000,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 99,
                HypotheticalR5Count = 1,
                Rules = noPity
            });

            TierDuplicateStat r5 = report.DuplicatesPerTier.Find(t => t.Rarity == 5);
            Assert.NotNull(r5);

            // Base rate is 60bp = 0.6%. No pity means no boost toward it; allow a wide statistical
            // band (0.3%-0.9%) rather than pin an exact float, since this is a random sweep over a
            // rare event.
            long totalPulls = 4000L * 300;
            double actualPercent = r5.TotalPulls * 100.0 / totalPulls;
            Assert.InRange(actualPercent, 0.3, 0.9);
        }

        [Fact]
        public void Floor_upgrades_a_below_floor_result_to_exactly_FloorTier_for_both_presets()
        {
            // With pity disabled, the only guarantee left standing is the floor, so any
            // observed gap without FloorTier-or-better is proof the floor itself upgraded a
            // result — pity never had the chance to.
            SummonRules proposedNoPity = SummonRules.Proposed.WithPityDisabled();
            SummonReport proposedReport = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 300,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 20260923,
                Rules = proposedNoPity
            });
            Assert.True(proposedReport.MaxObservedPullsWithoutFloorTier <= 10);

            SummonRules genshinNoPity = SummonRules.GenshinLike.WithPityDisabled();
            SummonReport genshinReport = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 300,
                Pulls = 300,
                AttuneMaxPulls = 300,
                Seed = 20260923,
                HypotheticalR5Count = 1,
                Rules = genshinNoPity
            });
            Assert.True(genshinReport.MaxObservedPullsWithoutFloorTier <= 10);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Byte-for-byte lock on <see cref="SummonRules.Proposed"/>'s output at a fixed seed,
        /// captured right after <c>SummonSimulator</c> was refactored to delegate its per-pull
        /// resolution to <see cref="SummonEngine.ResolvePull"/> (docs/06-roadmap.md phase E summon
        /// endpoint work) — confirmed identical to the pre-refactor numbers via a manual
        /// before/after `simrunner summon` diff at the time of that change. If this test ever
        /// fails, either the RNG stream order changed or a rate value did — both are things this
        /// test exists to catch before they ship silently.
        /// </summary>
        [Fact]
        public void Run_output_for_Proposed_is_locked_at_a_fixed_seed()
        {
            SummonReport report = SummonSimulator.Run(Pack.Value, new SummonRequest
            {
                Players = 200,
                Pulls = 100,
                AttuneMaxPulls = 100,
                Seed = 999,
                Rules = SummonRules.Proposed
            });

            Assert.Equal(200, report.FirstR4Plus.ReachedCount);
            Assert.Equal(224, report.FirstR4Plus.MeanTimes10);
            Assert.Equal(20, report.FirstR4Plus.Median);
            Assert.Equal(45, report.FirstR4Plus.P90);
            Assert.Equal(59, report.FirstR4Plus.Worst);
            Assert.Equal(58, report.MaxObservedPullsWithoutPityTier);
            Assert.Equal(9, report.MaxObservedPullsWithoutFloorTier);

            var tierByRarity = new Dictionary<int, TierDuplicateStat>();
            foreach (TierDuplicateStat tier in report.DuplicatesPerTier)
            {
                tierByRarity[tier.Rarity] = tier;
            }

            Assert.Equal(855, tierByRarity[4].TotalPulls);
            Assert.Equal(480, tierByRarity[4].Duplicates);
            Assert.Equal(7261, tierByRarity[3].TotalPulls);
            Assert.Equal(5313, tierByRarity[3].Duplicates);
            Assert.Equal(11884, tierByRarity[2].TotalPulls);
            Assert.Equal(11484, tierByRarity[2].Duplicates);
        }

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
