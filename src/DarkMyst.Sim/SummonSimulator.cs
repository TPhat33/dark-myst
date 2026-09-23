using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;

namespace DarkMyst.Sim
{
    /// <summary>Parameters for one <c>simrunner summon</c> run — see <see cref="SummonSimulator.Run"/>.</summary>
    public sealed class SummonRequest
    {
        public int Players { get; set; } = 10000;

        /// <summary>Pull budget per player for every "pulls to X" and duplicate-rate stat.</summary>
        public int Pulls { get; set; } = 300;

        /// <summary>
        /// Separate, larger pull budget used only by the Attune section: reaching the 300‰ cap
        /// needs many duplicates of one line, which routinely takes far more pulls than a
        /// realistic play-budget report should assume for the other sections (docs/12
        /// §"วิธีวัด").
        /// </summary>
        public int AttuneMaxPulls { get; set; } = 5000;

        public ulong Seed { get; set; } = 1;

        /// <summary>Synthetic rarity-5 stage-I lines added to the pool so the R5 row can be
        /// measured before the first real one is authored (docs/12 §"โครงชั้นความหายาก" —
        /// content 0.4.0 has none). 0 disables this entirely.</summary>
        public int HypotheticalR5Count { get; set; }

        public SummonRules Rules { get; set; } = SummonRules.Proposed;
    }

    /// <summary>Pulls-to-X distribution: mean/median/p90/worst over the players who reached it
    /// within budget, plus how many never did. <see cref="MeanTimes10"/> is the mean times 10
    /// (integer math only) so callers can print one decimal place without floating point.</summary>
    public sealed class PullCountStat
    {
        public int Players { get; set; }

        public int ReachedCount { get; set; }

        public int NeverReachedCount => Players - ReachedCount;

        public int MeanTimes10 { get; set; }

        public int Median { get; set; }

        public int P90 { get; set; }

        public int Worst { get; set; }
    }

    /// <summary>Pulls to first copy of one specific R4/R5 line, plus the share of players who
    /// still lacked it after <see cref="SummonRules.SparkThreshold"/> pulls — the players for
    /// whom Spark, not luck, is what would deliver it.</summary>
    public sealed class LineFirstPullStat
    {
        public string LineId { get; set; }

        public string Name { get; set; }

        public int Rarity { get; set; }

        public bool IsHypothetical { get; set; }

        public PullCountStat Stat { get; set; }

        /// <summary>Basis points of players still without this line at the Spark threshold, or
        /// null when the pull budget never reaches the threshold at all.</summary>
        public int? StillMissingAtSparkBasisPoints { get; set; }
    }

    public sealed class LineDuplicateStat
    {
        public string LineId { get; set; }

        public int Rarity { get; set; }

        public long TotalPulls { get; set; }

        public long Duplicates { get; set; }

        public int DuplicateRateBasisPoints => TotalPulls == 0 ? 0 : (int)(Duplicates * 10000 / TotalPulls);
    }

    public sealed class TierDuplicateStat
    {
        public int Rarity { get; set; }

        public long TotalPulls { get; set; }

        public long Duplicates { get; set; }

        public int DuplicateRateBasisPoints => TotalPulls == 0 ? 0 : (int)(Duplicates * 10000 / TotalPulls);
    }

    public sealed class SummonReport
    {
        public string ContentVersion { get; set; }

        public SummonRules Rules { get; set; }

        public int Players { get; set; }

        public int Pulls { get; set; }

        public int AttuneMaxPulls { get; set; }

        public ulong Seed { get; set; }

        public int HypotheticalR5Count { get; set; }

        /// <summary>One line per tier with no pullable stage-I line right now, naming where its
        /// probability goes instead (docs/12 §"โครงชั้นความหายาก").</summary>
        public List<string> EmptyTierWarnings { get; set; } = new List<string>();

        public PullCountStat FirstR4Plus { get; set; }

        public PullCountStat FirstR5 { get; set; }

        public PullCountStat FirstR4Exact { get; set; }

        /// <summary>One entry per R4/R5 line (real and hypothetical), sorted rarity desc then id.</summary>
        public List<LineFirstPullStat> PerLineFirstPull { get; set; } = new List<LineFirstPullStat>();

        /// <summary>One entry per pullable line of any tier, sorted by id.</summary>
        public List<LineDuplicateStat> DuplicatesPerLine { get; set; } = new List<LineDuplicateStat>();

        /// <summary>One entry per tier that has at least one pullable line.</summary>
        public List<TierDuplicateStat> DuplicatesPerTier { get; set; } = new List<TierDuplicateStat>();

        public PullCountStat FirstAttuneCapAnyLine { get; set; }

        /// <summary>Keyed by rarity (2/3/4/5 — 5 is only populated with hypothetical lines): pulls
        /// until the first line of that tier reaches the Attune cap.</summary>
        public Dictionary<int, PullCountStat> FirstAttuneCapByTier { get; set; } = new Dictionary<int, PullCountStat>();

        /// <summary>Largest number of consecutive pulls observed anywhere in the whole sweep
        /// without an R4+ result — the measured proof that hard pity (<see cref="SummonRules.HardPityPull"/>)
        /// actually bounds the worst case, not just the formula.</summary>
        public int MaxObservedPullsWithoutR4Plus { get; set; }

        /// <summary>Same, for R3+ against the 10-pull floor.</summary>
        public int MaxObservedPullsWithoutR3Plus { get; set; }
    }

    /// <summary>
    /// Simulates the character summon system end to end (docs/12-summon-spec.md §"วิธีวัด") so
    /// every rate the doc quotes is reproducible by a command, never a feeling: roll a tier with
    /// the three-layer guarantee (10-pull floor, soft pity from pull 40, hard pity at pull 60),
    /// resolve it to an actual pullable stage-I line (folding empty tiers down, per
    /// §"โครงชั้นความหายาก"), track duplicates into Echo shards, and Attune them toward the 300‰
    /// cap. No combat, no stats — <c>DarkMyst.Combat</c> is untouched by this file, matching the
    /// doc's own note that the summon system only ever hands out "access", never power.
    /// </summary>
    public static class SummonSimulator
    {
        private const ulong PlayerStreamBase = 0x5_0000_0000UL;

        public static SummonReport Run(ContentPack pack, SummonRequest request)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Players <= 0)
            {
                throw new ArgumentException("Players must be positive.", nameof(request));
            }

            if (request.Pulls <= 0)
            {
                throw new ArgumentException("Pulls must be positive.", nameof(request));
            }

            if (request.AttuneMaxPulls <= 0)
            {
                throw new ArgumentException("AttuneMaxPulls must be positive.", nameof(request));
            }

            if (request.HypotheticalR5Count < 0)
            {
                throw new ArgumentException("HypotheticalR5Count cannot be negative.", nameof(request));
            }

            SummonRules rules = request.Rules ?? SummonRules.Proposed;
            Dictionary<int, List<CharacterData>> poolByTier = BuildPool(pack, request.HypotheticalR5Count);
            List<string> emptyTierWarnings = BuildEmptyTierWarnings(poolByTier);
            int attuneCapPerMille = pack.Progression?.Evolve?.InheritedBonusCapPerMille
                ?? rules.DefaultAttuneCapPerMille;

            List<CharacterData> trackedLines = BuildTrackedLines(poolByTier);

            var firstR4Plus = new List<int>();
            var firstR5 = new List<int>();
            var firstR4Exact = new List<int>();
            var perLineReached = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var perLineMissingAtSpark = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CharacterData line in trackedLines)
            {
                perLineReached[line.Id] = new List<int>();
                perLineMissingAtSpark[line.Id] = 0;
            }

            var pullsPerLine = new Dictionary<string, long>(StringComparer.Ordinal);
            var duplicatesPerLine = new Dictionary<string, long>(StringComparer.Ordinal);
            var pullsPerTier = new Dictionary<int, long>();
            var duplicatesPerTier = new Dictionary<int, long>();
            for (int tier = 2; tier <= 5; tier++)
            {
                pullsPerTier[tier] = 0;
                duplicatesPerTier[tier] = 0;
            }

            var firstAttuneCapAny = new List<int>();
            var firstAttuneCapByTier = new Dictionary<int, List<int>>
            {
                { 2, new List<int>() }, { 3, new List<int>() }, { 4, new List<int>() }, { 5, new List<int>() }
            };

            int maxGapR4Plus = 0;
            int maxGapR3Plus = 0;

            int maxPullsPerPlayer = Math.Max(request.Pulls, request.AttuneMaxPulls);

            for (int player = 0; player < request.Players; player++)
            {
                // One seed, one PCG stream per player. Deriving the seed as Seed + player would
                // make run S and run S+1 share all but one player, so two "independent" seeds
                // would agree almost exactly and hide real variance.
                var rng = new DeterministicRandom(request.Seed, PlayerStreamBase + (ulong)player);

                int pullsSinceLastR4Plus = 0;
                int pullsSinceLastR3Plus = 0;

                var ownedLines = new HashSet<string>(StringComparer.Ordinal);
                var shardsByLine = new Dictionary<string, int>(StringComparer.Ordinal);
                var cappedLines = new HashSet<string>(StringComparer.Ordinal);

                int? playerFirstR4Plus = null;
                int? playerFirstR5 = null;
                int? playerFirstR4Exact = null;
                var playerFirstLinePull = new Dictionary<string, int?>(StringComparer.Ordinal);
                foreach (CharacterData line in trackedLines)
                {
                    playerFirstLinePull[line.Id] = null;
                }

                int? playerFirstAttuneCapAny = null;
                var playerFirstAttuneCapByTier = new Dictionary<int, int?> { { 2, null }, { 3, null }, { 4, null }, { 5, null } };

                for (int i = 1; i <= maxPullsPerPlayer; i++)
                {
                    int k = pullsSinceLastR4Plus + 1;
                    int r4PlusBp = rules.ComputeR4PlusChanceBasisPoints(k);
                    int roll1 = rng.NextInt(0, 10000);

                    int rolledTier;
                    if (roll1 < r4PlusBp)
                    {
                        int roll2 = rng.NextInt(0, rules.R4PlusBaseBasisPoints);
                        rolledTier = roll2 < rules.R5RateBasisPoints ? 5 : 4;
                    }
                    else
                    {
                        int roll3 = rng.NextInt(0, rules.R3RateBasisPoints + rules.R2RateBasisPoints);
                        rolledTier = roll3 < rules.R3RateBasisPoints ? 3 : 2;
                    }

                    if (rolledTier < 3 && pullsSinceLastR3Plus >= rules.FloorWindowPulls)
                    {
                        rolledTier = 3;
                    }

                    CharacterData picked = PickFromPool(poolByTier, rolledTier, rng);
                    int actualTier = picked.Rarity;

                    // Pity reacts to what the player actually received, not the die roll that may
                    // have been folded down to a lower, non-empty tier (docs/12
                    // §"โครงชั้นความหายาก"). In content 0.4.0 only R5 is ever empty and it always
                    // folds to R4, so this never actually demotes a roll below R4+; written this
                    // way so a future empty R4 tier would not silently break the guarantee.
                    if (actualTier >= 4)
                    {
                        pullsSinceLastR4Plus = 0;
                    }
                    else
                    {
                        pullsSinceLastR4Plus++;
                        if (pullsSinceLastR4Plus > maxGapR4Plus)
                        {
                            maxGapR4Plus = pullsSinceLastR4Plus;
                        }
                    }

                    if (actualTier >= 3)
                    {
                        pullsSinceLastR3Plus = 0;
                    }
                    else
                    {
                        pullsSinceLastR3Plus++;
                        if (pullsSinceLastR3Plus > maxGapR3Plus)
                        {
                            maxGapR3Plus = pullsSinceLastR3Plus;
                        }
                    }

                    bool withinMainBudget = i <= request.Pulls;
                    bool withinAttuneBudget = i <= request.AttuneMaxPulls;

                    if (withinMainBudget)
                    {
                        if (actualTier >= 4 && playerFirstR4Plus == null)
                        {
                            playerFirstR4Plus = i;
                        }

                        if (actualTier == 5 && playerFirstR5 == null)
                        {
                            playerFirstR5 = i;
                        }

                        if (actualTier == 4 && playerFirstR4Exact == null)
                        {
                            playerFirstR4Exact = i;
                        }
                    }

                    bool isDuplicate = !ownedLines.Add(picked.Id);

                    if (withinMainBudget)
                    {
                        pullsPerLine.TryGetValue(picked.Id, out long lineTotal);
                        pullsPerLine[picked.Id] = lineTotal + 1;
                        pullsPerTier[actualTier] = pullsPerTier[actualTier] + 1;

                        if (isDuplicate)
                        {
                            duplicatesPerLine.TryGetValue(picked.Id, out long lineDup);
                            duplicatesPerLine[picked.Id] = lineDup + 1;
                            duplicatesPerTier[actualTier] = duplicatesPerTier[actualTier] + 1;
                        }
                        else if (playerFirstLinePull.ContainsKey(picked.Id) && playerFirstLinePull[picked.Id] == null)
                        {
                            playerFirstLinePull[picked.Id] = i;
                        }
                    }

                    if (isDuplicate)
                    {
                        int gain = rules.DuplicateShardsForRarity(actualTier);
                        shardsByLine.TryGetValue(picked.Id, out int shardsSoFar);
                        int newShards = shardsSoFar + gain;
                        shardsByLine[picked.Id] = newShards;

                        int permille = newShards / rules.ShardsPerPerMille;
                        if (permille > attuneCapPerMille)
                        {
                            permille = attuneCapPerMille;
                        }

                        if (permille >= attuneCapPerMille && cappedLines.Add(picked.Id) && withinAttuneBudget)
                        {
                            if (playerFirstAttuneCapAny == null)
                            {
                                playerFirstAttuneCapAny = i;
                            }

                            if (playerFirstAttuneCapByTier.TryGetValue(actualTier, out int? existing) && existing == null)
                            {
                                playerFirstAttuneCapByTier[actualTier] = i;
                            }
                        }
                    }
                }

                if (playerFirstR4Plus.HasValue)
                {
                    firstR4Plus.Add(playerFirstR4Plus.Value);
                }

                if (playerFirstR5.HasValue)
                {
                    firstR5.Add(playerFirstR5.Value);
                }

                if (playerFirstR4Exact.HasValue)
                {
                    firstR4Exact.Add(playerFirstR4Exact.Value);
                }

                foreach (CharacterData line in trackedLines)
                {
                    int? reached = playerFirstLinePull[line.Id];
                    if (reached.HasValue)
                    {
                        perLineReached[line.Id].Add(reached.Value);
                    }

                    if (request.Pulls >= rules.SparkThreshold
                        && (!reached.HasValue || reached.Value > rules.SparkThreshold))
                    {
                        perLineMissingAtSpark[line.Id] = perLineMissingAtSpark[line.Id] + 1;
                    }
                }

                if (playerFirstAttuneCapAny.HasValue)
                {
                    firstAttuneCapAny.Add(playerFirstAttuneCapAny.Value);
                }

                foreach (KeyValuePair<int, int?> tier in playerFirstAttuneCapByTier)
                {
                    if (tier.Value.HasValue)
                    {
                        firstAttuneCapByTier[tier.Key].Add(tier.Value.Value);
                    }
                }
            }

            var report = new SummonReport
            {
                ContentVersion = pack.Version,
                Rules = rules,
                Players = request.Players,
                Pulls = request.Pulls,
                AttuneMaxPulls = request.AttuneMaxPulls,
                Seed = request.Seed,
                HypotheticalR5Count = request.HypotheticalR5Count,
                EmptyTierWarnings = emptyTierWarnings,
                FirstR4Plus = Summarize(firstR4Plus, request.Players),
                FirstR5 = Summarize(firstR5, request.Players),
                FirstR4Exact = Summarize(firstR4Exact, request.Players),
                FirstAttuneCapAnyLine = Summarize(firstAttuneCapAny, request.Players),
                MaxObservedPullsWithoutR4Plus = maxGapR4Plus,
                MaxObservedPullsWithoutR3Plus = maxGapR3Plus
            };

            foreach (CharacterData line in trackedLines)
            {
                report.PerLineFirstPull.Add(new LineFirstPullStat
                {
                    LineId = line.Id,
                    Name = line.Name,
                    Rarity = line.Rarity,
                    IsHypothetical = IsHypotheticalId(line.Id),
                    Stat = Summarize(perLineReached[line.Id], request.Players),
                    StillMissingAtSparkBasisPoints = request.Pulls >= rules.SparkThreshold
                        ? (int?)(perLineMissingAtSpark[line.Id] * 10000L / request.Players)
                        : null
                });
            }

            var allLineIds = new List<string>(pullsPerLine.Keys);
            allLineIds.Sort(StringComparer.Ordinal);
            foreach (string lineId in allLineIds)
            {
                duplicatesPerLine.TryGetValue(lineId, out long dup);
                report.DuplicatesPerLine.Add(new LineDuplicateStat
                {
                    LineId = lineId,
                    Rarity = FindRarity(poolByTier, lineId),
                    TotalPulls = pullsPerLine[lineId],
                    Duplicates = dup
                });
            }

            for (int tier = 5; tier >= 2; tier--)
            {
                if (pullsPerTier[tier] == 0 && poolByTier[tier].Count == 0)
                {
                    continue;
                }

                report.DuplicatesPerTier.Add(new TierDuplicateStat
                {
                    Rarity = tier,
                    TotalPulls = pullsPerTier[tier],
                    Duplicates = duplicatesPerTier[tier]
                });

                report.FirstAttuneCapByTier[tier] = Summarize(firstAttuneCapByTier[tier], request.Players);
            }

            return report;
        }

        private static Dictionary<int, List<CharacterData>> BuildPool(ContentPack pack, int hypotheticalR5Count)
        {
            var poolByTier = new Dictionary<int, List<CharacterData>>
            {
                { 2, new List<CharacterData>() },
                { 3, new List<CharacterData>() },
                { 4, new List<CharacterData>() },
                { 5, new List<CharacterData>() }
            };

            foreach (CharacterData character in pack.Characters)
            {
                if (character.EvolveStage == 1 && poolByTier.ContainsKey(character.Rarity))
                {
                    poolByTier[character.Rarity].Add(character);
                }
            }

            for (int i = 1; i <= hypotheticalR5Count; i++)
            {
                poolByTier[5].Add(new CharacterData
                {
                    Id = "hypothetical-r5-" + i,
                    LineId = "hypothetical-r5-" + i,
                    Name = "Hypothetical R5 #" + i,
                    Affinity = Affinity.Neutral,
                    Role = "Hypothetical",
                    Rarity = 5,
                    EvolveStage = 1
                });
            }

            foreach (List<CharacterData> lines in poolByTier.Values)
            {
                lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            return poolByTier;
        }

        private static List<string> BuildEmptyTierWarnings(Dictionary<int, List<CharacterData>> poolByTier)
        {
            var warnings = new List<string>();
            for (int tier = 5; tier >= 2; tier--)
            {
                if (poolByTier[tier].Count > 0)
                {
                    continue;
                }

                int fold = tier - 1;
                while (fold >= 2 && poolByTier[fold].Count == 0)
                {
                    fold--;
                }

                string target = fold >= 2 ? "R" + fold : "NOTHING (no pullable line at all)";
                warnings.Add(
                    "R" + tier + " has no pullable stage-I line in this content pack — its probability"
                    + " folds down to " + target + ".");
            }

            return warnings;
        }

        private static List<CharacterData> BuildTrackedLines(Dictionary<int, List<CharacterData>> poolByTier)
        {
            var lines = new List<CharacterData>();
            lines.AddRange(poolByTier[5]);
            lines.AddRange(poolByTier[4]);
            return lines;
        }

        private static bool IsHypotheticalId(string id)
        {
            return id.StartsWith("hypothetical-r5-", StringComparison.Ordinal);
        }

        private static int FindRarity(Dictionary<int, List<CharacterData>> poolByTier, string lineId)
        {
            foreach (KeyValuePair<int, List<CharacterData>> tier in poolByTier)
            {
                foreach (CharacterData character in tier.Value)
                {
                    if (character.Id == lineId)
                    {
                        return character.Rarity;
                    }
                }
            }

            throw new InvalidOperationException("Line '" + lineId + "' was pulled but is not in any tier's pool.");
        }

        /// <summary>Resolves a rolled tier to an actual pullable character, folding down through
        /// lower tiers when the rolled one has no pullable stage-I line (docs/12
        /// §"โครงชั้นความหายาก"). Always draws exactly one RNG word to pick within the resolved
        /// tier, even for a single-candidate tier — see <see cref="DeterministicRandom.NextInt"/>'s
        /// own note on why that word is never skipped.</summary>
        private static CharacterData PickFromPool(Dictionary<int, List<CharacterData>> poolByTier, int rolledTier, DeterministicRandom rng)
        {
            for (int tier = rolledTier; tier >= 2; tier--)
            {
                List<CharacterData> pool = poolByTier[tier];
                if (pool.Count > 0)
                {
                    int index = rng.NextInt(0, pool.Count);
                    return pool[index];
                }
            }

            throw new InvalidOperationException(
                "No pullable stage-I line exists at or below rolled tier R" + rolledTier + ".");
        }

        private static PullCountStat Summarize(List<int> values, int totalPlayers)
        {
            var stat = new PullCountStat { Players = totalPlayers, ReachedCount = values.Count };
            if (values.Count == 0)
            {
                return stat;
            }

            var sorted = new List<int>(values);
            sorted.Sort();

            long sum = 0;
            foreach (int v in sorted)
            {
                sum += v;
            }

            stat.MeanTimes10 = (int)(sum * 10 / sorted.Count);
            stat.Median = NearestRank(sorted, 50);
            stat.P90 = NearestRank(sorted, 90);
            stat.Worst = sorted[sorted.Count - 1];
            return stat;
        }

        /// <summary>Nearest-rank percentile on a sorted array: rank = ceil(p/100 * n), integer
        /// math only.</summary>
        private static int NearestRank(List<int> sortedValues, int percentile)
        {
            int n = sortedValues.Count;
            int rank = (percentile * n + 99) / 100;
            if (rank < 1)
            {
                rank = 1;
            }

            if (rank > n)
            {
                rank = n;
            }

            return sortedValues[rank - 1];
        }
    }
}
