using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using DarkMyst.Expedition;
using DarkMyst.Expedition.Model;
using DarkMyst.Sim;

namespace DarkMyst.SimRunner
{
    /// <summary>
    /// Runs battles from the command line so combat can be balanced and debugged long before
    /// there is a client to look at. Modes:
    /// <list type="bullet">
    /// <item><description><c>validate</c> — load a content pack and report every problem.</description></item>
    /// <item><description><c>battle</c> — run one fight and print the event log.</description></item>
    /// <item><description><c>sweep</c> — run many seeds and report the win rate and length.</description></item>
    /// <item><description><c>expedition</c> — auto-play one expedition stage end to end, or
    /// sweep many seeds of it and report the farm loop's shape.</description></item>
    /// <item><description><c>matrix</c> — run several named rosters against several encounters
    /// (or against each other) and print a roster × opponent table, to answer "is there a
    /// dominant team", "is any of the roster dead content" and "is a loss recoverable" with
    /// numbers instead of a feeling. See <c>docs/07-testing-plan.md</c>.</description></item>
    /// <item><description><c>roster</c> — print the content pack's composition (affinity ×
    /// playable line, affinity × enemy unit, role × rarity) so a shape like "every enemy is
    /// Umbral" or "the top rarity tier is all Menders" is visible before it ships, not after
    /// someone measures a 30-point win-rate swing. Enforced in CI by
    /// <c>DarkMyst.Content.Tests/RosterShapeTests.cs</c>; this command is the human-readable
    /// view of the same counts.</description></item>
    /// <item><description><c>summon</c> — simulate the character summon system
    /// (docs/12-summon-spec.md §"วิธีวัด") across many players and report pulls-to-first-R4/R5,
    /// per-line first-pull and duplicate rates, and pulls until Attune caps a line at 300‰. Rates
    /// come from <c>DarkMyst.Sim.SummonRules.Proposed</c>, which docs/12 states plainly are not
    /// locked.</description></item>
    /// </list>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (ContentException ex)
            {
                Console.Error.WriteLine("Content error: " + ex.Message);
                return 2;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Invalid battle: " + ex.Message);
                return 2;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var options = Options.Parse(args);
            ContentPack pack = ContentPack.LoadFromDirectory(options.ContentDirectory);

            switch (options.Command)
            {
                case "validate":
                    Console.WriteLine(
                        "Content " + pack.Version + " is valid for rules " + CombatRules.Version + ": "
                        + pack.Characters.Count + " characters, " + pack.Enemies.Count + " enemies, "
                        + pack.Encounters.Count + " encounters, " + pack.Skills.Count + " skills, "
                        + pack.Stages.Count + " expedition stages, " + pack.RewardTables.Count + " reward tables, "
                        + pack.Events.Count + " events.");
                    return 0;

                case "battle":
                    return RunBattle(pack, options);

                case "sweep":
                    return RunSweep(pack, options);

                case "expedition":
                    return RunExpedition(pack, options);

                case "matrix":
                    return RunMatrix(pack, options);

                case "roster":
                    return RunRoster(pack);

                case "summon":
                    return RunSummon(pack, options);

                default:
                    Console.Error.WriteLine("Unknown command '" + options.Command + "'.");
                    PrintUsage();
                    return 1;
            }
        }

        private static int RunBattle(ContentPack pack, Options options)
        {
            BattleResult result = Simulate(pack, options, options.Seed);

            Console.WriteLine("Encounter : " + options.EncounterId);
            Console.WriteLine("Seed      : " + options.Seed);
            Console.WriteLine("Rules     : " + result.RulesVersion + "  Content: " + result.ContentVersion);
            Console.WriteLine();

            foreach (BattleEvent evt in result.Events)
            {
                Console.WriteLine(Describe(evt));
            }

            Console.WriteLine();
            Console.WriteLine("Outcome   : " + result.Outcome + " (" + result.EndReason + ") after "
                              + result.Rounds + " rounds");
            Console.WriteLine("RNG calls : " + result.RngCalls);
            Console.WriteLine("Checksum  : " + result.Checksum);

            // A second identical run is cheap and catches an accidental dependency on
            // dictionary ordering or shared mutable state the moment it is introduced.
            BattleResult replay = Simulate(pack, options, options.Seed);
            if (replay.Checksum != result.Checksum)
            {
                Console.Error.WriteLine("DETERMINISM FAILURE: replay checksum " + replay.Checksum);
                return 3;
            }

            return 0;
        }

        /// <summary>
        /// Delegates the actual sweep loop to <c>DarkMyst.Sim.BattleSweepRunner</c> — the same
        /// implementation the admin tool's sweep button calls (docs/11-admin-spec.md) — and prints
        /// the exact text this command has always printed. This CLI's behaviour and output must
        /// not change from extracting the shared library; only the printing stays here.
        /// </summary>
        private static int RunSweep(ContentPack pack, Options options)
        {
            SweepResult result = BattleSweepRunner.Run(pack, new SweepRequest
            {
                EncounterId = options.EncounterId,
                Roster = options.Roster,
                Level = options.Level,
                Seed = options.Seed,
                Repeat = options.Repeat
            });

            Console.WriteLine("Encounter : " + result.EncounterId);
            Console.WriteLine("Battles   : " + result.Battles + " (seeds " + options.Seed + "..+"
                              + (result.Battles - 1) + ")");
            Console.WriteLine("Win rate  : " + Percent(result.Wins, result.Battles)
                              + "   draws " + Percent(result.Draws, result.Battles));
            Console.WriteLine("Avg length: " + result.AverageRounds.ToString("0.0", CultureInfo.InvariantCulture)
                              + " rounds");
            Console.WriteLine();
            Console.WriteLine("Survival by character:");

            foreach (SweepSurvivorEntry entry in result.Survivors)
            {
                Console.WriteLine("  " + entry.CharacterId.PadRight(28) + Percent(entry.Survived, result.Battles));
            }

            return 0;
        }

        private static int RunExpedition(ContentPack pack, Options options)
        {
            if (options.Repeat <= 1)
            {
                RunExpeditionOnce(pack, options, options.Seed, verbose: true);
                return 0;
            }

            return RunExpeditionSweep(pack, options, options.Repeat);
        }

        /// <summary>Plays one run end to end with a simple, deterministic auto-pick policy and,
        /// when <paramref name="verbose"/>, prints every node as it resolves.</summary>
        private static ExpeditionRun RunExpeditionOnce(ContentPack pack, Options options, ulong seed, bool verbose)
        {
            StageData stage = pack.GetStage(options.StageId);
            ExpeditionRun run = ExpeditionRun.Start(
                pack, options.StageId, seed, leaderSlot: 0, BuildPlacements(pack, options));

            if (verbose)
            {
                Console.WriteLine("Stage     : " + stage.Id + " (" + stage.Name + ")");
                Console.WriteLine("Seed      : " + seed);
                Console.WriteLine("Content   : " + pack.Version + "   Rules: " + CombatRules.Version);
                Console.WriteLine();
            }

            IReadOnlyList<ExpeditionChoice> choices = run.AvailableChoices();
            while (choices.Count > 0)
            {
                int pick = AutoPick(run, choices);
                ExpeditionNodeOutcome outcome = run.Choose(pack, pick);

                if (verbose)
                {
                    Console.WriteLine(DescribeOutcome(run, outcome));
                }

                choices = run.AvailableChoices();
            }

            if (verbose)
            {
                Console.WriteLine();
                Console.WriteLine("Result    : " + run.State.Status + " after " + run.State.Log.Count + " node(s)");
                Console.WriteLine("Banked    : gold=" + run.State.BankedGold + "  materials: " + DescribeMaterials(run));
                if (run.State.BankedCharacterIds.Count > 0)
                {
                    Console.WriteLine("Characters: " + string.Join(", ", run.State.BankedCharacterIds));
                }
            }

            return run;
        }

        private static int RunExpeditionSweep(ContentPack pack, Options options, int repeat)
        {
            StageData stage = pack.GetStage(options.StageId);

            int cleared = 0;
            long totalNodes = 0;
            long totalGold = 0;
            var totalMaterials = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalCharacterDrops = 0;

            for (int i = 0; i < repeat; i++)
            {
                ExpeditionRun run = RunExpeditionOnce(pack, options, options.Seed + (ulong)i, verbose: false);

                if (run.State.Status == RunStatus.Cleared)
                {
                    cleared++;
                }

                totalNodes += run.State.Log.Count;
                totalGold += run.State.BankedGold;
                totalCharacterDrops += run.State.BankedCharacterIds.Count;

                foreach (MaterialStack stack in run.State.BankedMaterials)
                {
                    totalMaterials.TryGetValue(stack.MaterialId, out int soFar);
                    totalMaterials[stack.MaterialId] = soFar + stack.Amount;
                }
            }

            Console.WriteLine("Stage         : " + stage.Id + " (" + stage.Name + ")");
            Console.WriteLine("Runs          : " + repeat + " (seeds " + options.Seed + "..+" + (repeat - 1) + ")");
            Console.WriteLine("Completion    : " + Percent(cleared, repeat));
            Console.WriteLine(
                "Avg nodes     : " + (totalNodes / (double)repeat).ToString("0.0", CultureInfo.InvariantCulture));
            Console.WriteLine(
                "Avg gold/run  : " + (totalGold / (double)repeat).ToString("0.0", CultureInfo.InvariantCulture));
            Console.WriteLine(
                "Avg chars/run : " + (totalCharacterDrops / (double)repeat).ToString("0.00", CultureInfo.InvariantCulture));
            Console.WriteLine("Avg materials/run:");

            var materialIds = new List<string>(totalMaterials.Keys);
            materialIds.Sort(StringComparer.Ordinal);
            foreach (string materialId in materialIds)
            {
                Console.WriteLine(
                    "  " + materialId.PadRight(20)
                    + (totalMaterials[materialId] / (double)repeat).ToString("0.00", CultureInfo.InvariantCulture));
            }

            return 0;
        }

        // ------------------------------------------------------------------
        // roster
        // ------------------------------------------------------------------

        /// <summary>
        /// Prints the content pack's composition along the three axes a designer needs to check
        /// before adding a character: affinity coverage on both sides of the fight (can every
        /// affinity actually attack and be attacked with advantage somewhere), and which role
        /// sits at the top rarity tier (the tier a summon system would pull from most, so
        /// concentrating it on one role is a pay-to-win shape even when nobody intends it — see
        /// docs/07-testing-plan.md). Scoped to <em>stage-I</em> characters for the line/rarity
        /// axes: a stage II/III form is reached by evolving, never pulled or rewarded directly
        /// (see <c>drop_ashfields_treasure</c> in stages.json, which only ever grants a stage-I
        /// id), so it is not part of "what a player can obtain" and would only dilute the count
        /// that actually matters for pull-rate fairness.
        /// </summary>
        private static int RunRoster(ContentPack pack)
        {
            List<CharacterData> stageOneLines = StageOneCharacters(pack);

            Console.WriteLine("Roster composition (content " + pack.Version + ")");
            Console.WriteLine(new string('=', 40));
            Console.WriteLine();

            PrintAffinityLines(stageOneLines);
            Console.WriteLine();
            PrintAffinityEnemies(pack);
            Console.WriteLine();
            PrintRoleByRarity(stageOneLines);

            return 0;
        }

        private static List<CharacterData> StageOneCharacters(ContentPack pack)
        {
            var lines = new List<CharacterData>();
            foreach (CharacterData character in pack.Characters)
            {
                if (character.EvolveStage == 1)
                {
                    lines.Add(character);
                }
            }

            lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return lines;
        }

        private static void PrintAffinityLines(List<CharacterData> stageOneLines)
        {
            var byAffinity = new Dictionary<Affinity, List<string>>();
            foreach (Affinity affinity in (Affinity[])Enum.GetValues(typeof(Affinity)))
            {
                byAffinity[affinity] = new List<string>();
            }

            foreach (CharacterData line in stageOneLines)
            {
                byAffinity[line.Affinity].Add(line.Name);
            }

            Console.WriteLine(
                "Affinity x playable stage-I lines (" + stageOneLines.Count
                + " lines — what a summon or reward can hand a player):");
            foreach (Affinity affinity in (Affinity[])Enum.GetValues(typeof(Affinity)))
            {
                List<string> names = byAffinity[affinity];
                Console.WriteLine(
                    "  " + affinity.ToString().PadRight(10) + names.Count.ToString().PadLeft(3) + "  "
                    + (names.Count == 0 ? "(none)" : string.Join(", ", names)));
            }
        }

        private static void PrintAffinityEnemies(ContentPack pack)
        {
            var counts = new Dictionary<Affinity, int>();
            foreach (Affinity affinity in (Affinity[])Enum.GetValues(typeof(Affinity)))
            {
                counts[affinity] = 0;
            }

            int total = 0;
            foreach (EncounterData encounter in pack.Encounters)
            {
                foreach (EncounterUnitData unit in encounter.Units)
                {
                    EnemyData enemy = pack.GetEnemy(unit.EnemyId);
                    counts[enemy.Affinity]++;
                    total++;
                }
            }

            Console.WriteLine(
                "Affinity x enemy units (every unit in every encounter, " + total + " total):");
            foreach (Affinity affinity in (Affinity[])Enum.GetValues(typeof(Affinity)))
            {
                int count = counts[affinity];
                double share = total == 0 ? 0.0 : count * 100.0 / total;
                Console.WriteLine(
                    "  " + affinity.ToString().PadRight(10) + count.ToString().PadLeft(3) + "  "
                    + share.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + "%");
            }
        }

        private static void PrintRoleByRarity(List<CharacterData> stageOneLines)
        {
            var byRarity = new SortedDictionary<int, Dictionary<string, int>>();
            int topRarity = 0;
            foreach (CharacterData line in stageOneLines)
            {
                if (!byRarity.TryGetValue(line.Rarity, out Dictionary<string, int> roles))
                {
                    roles = new Dictionary<string, int>(StringComparer.Ordinal);
                    byRarity[line.Rarity] = roles;
                }

                roles.TryGetValue(line.Role, out int soFar);
                roles[line.Role] = soFar + 1;

                if (line.Rarity > topRarity)
                {
                    topRarity = line.Rarity;
                }
            }

            Console.WriteLine("Role x rarity (stage-I lines only):");
            foreach (KeyValuePair<int, Dictionary<string, int>> tier in byRarity)
            {
                var roleNames = new List<string>(tier.Value.Keys);
                roleNames.Sort(StringComparer.Ordinal);

                var parts = new List<string>();
                foreach (string role in roleNames)
                {
                    parts.Add(role + " x" + tier.Value[role]);
                }

                string marker = tier.Key == topRarity ? "  <- top tier" : "";
                Console.WriteLine("  rarity " + tier.Key + ": " + string.Join(", ", parts) + marker);
            }

            if (byRarity.TryGetValue(topRarity, out Dictionary<string, int> topRoles))
            {
                int tierTotal = 0;
                int maxInOneRole = 0;
                foreach (int roleCount in topRoles.Values)
                {
                    tierTotal += roleCount;
                    if (roleCount > maxInOneRole)
                    {
                        maxInOneRole = roleCount;
                    }
                }

                double concentration = tierTotal == 0 ? 0.0 : maxInOneRole * 100.0 / tierTotal;
                Console.WriteLine();
                Console.WriteLine(
                    "Top rarity tier (" + topRarity + "): " + tierTotal + " line(s) across "
                    + topRoles.Count + " role(s), most-concentrated role is "
                    + concentration.ToString("0.0", CultureInfo.InvariantCulture)
                    + "% of the tier.");
            }
        }

        // ------------------------------------------------------------------
        // summon
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs <c>DarkMyst.Sim.SummonSimulator</c> and prints its report (docs/12-summon-spec.md
        /// §"วิธีวัด"). All the arithmetic lives in the library; this only formats it, the same
        /// split <see cref="RunSweep"/> keeps with <c>BattleSweepRunner</c>.
        /// </summary>
        private static int RunSummon(ContentPack pack, Options options)
        {
            if (options.SummonCompare)
            {
                return RunSummonCompare(pack, options);
            }

            SummonRules rules = ResolveSummonRules(options.SummonRulesPreset);
            SummonReport report = SummonSimulator.Run(pack, BuildSummonRequest(options, rules));

            PrintSummonHeader(report, SummonRulesPresetName(options.SummonRulesPreset));
            Console.WriteLine();
            PrintSummonMilestones(report);
            Console.WriteLine();
            PrintSummonPerLine(report);
            Console.WriteLine();
            PrintSummonDuplicates(report);
            Console.WriteLine();
            PrintSummonAttune(report);

            return 0;
        }

        private static SummonRequest BuildSummonRequest(Options options, SummonRules rules)
        {
            return new SummonRequest
            {
                Players = options.SummonPlayers,
                Pulls = options.SummonPulls,
                AttuneMaxPulls = options.SummonAttuneMaxPulls,
                Seed = options.Seed,
                HypotheticalR5Count = options.SummonHypotheticalR5,
                Rules = rules
            };
        }

        private static SummonRules ResolveSummonRules(string preset)
        {
            switch (preset)
            {
                case "genshin-like": return SummonRules.GenshinLike;
                case "proposed":
                default: return SummonRules.Proposed;
            }
        }

        private static string SummonRulesPresetName(string preset)
        {
            return preset == "genshin-like" ? "GenshinLike" : "Proposed";
        }

        private static void PrintSummonHeader(SummonReport report, string presetName)
        {
            Console.WriteLine("Summon simulation (content " + report.ContentVersion + ")");
            Console.WriteLine(new string('=', 40));
            Console.WriteLine("Preset    : " + presetName);
            Console.WriteLine(
                presetName == "GenshinLike"
                    ? "FOR COMPARISON ONLY — Genshin Impact's public wish structure expressed in this"
                      + " model's shape (docs/12-summon-spec.md), not a DarkMyst proposal."
                    : "NOT LOCKED — every rate below is DarkMyst.Sim.SummonRules.Proposed, which mirrors"
                      + " docs/12-summon-spec.md but is explicitly not locked pending phase-C farm data.");
            // "-> R{tier}+" only appears for a rule set whose floor/pity tier differs from
            // Proposed's implicit R3/R4 — this keeps Proposed's line byte-identical to before
            // PityTier/FloorTier existed, while GenshinLike (which floors/pities on different
            // tiers) says so explicitly.
            string floorTierSuffix = report.Rules.FloorTier == 3 ? "" : " -> R" + report.Rules.FloorTier + "+";
            string pityTierSuffix = report.Rules.PityTier == 4 ? "" : " -> R" + report.Rules.PityTier + "+";
            Console.WriteLine(
                "Rules     : R5 " + FormatBasisPointsPercent(report.Rules.R5RateBasisPoints)
                + "  R4 " + FormatBasisPointsPercent(report.Rules.R4RateBasisPoints)
                + "  R3 " + FormatBasisPointsPercent(report.Rules.R3RateBasisPoints)
                + "  R2 " + FormatBasisPointsPercent(report.Rules.R2RateBasisPoints)
                + "  | floor every " + (report.Rules.FloorWindowPulls + 1) + " pulls" + floorTierSuffix
                + "  | soft pity from pull " + report.Rules.SoftPityStartPull
                + " (+" + FormatBasisPointsPercent(report.Rules.SoftPityBasisPointsPerStep) + "/pull)"
                + "  | hard pity at pull " + report.Rules.HardPityPull + pityTierSuffix
                + "  | spark at " + report.Rules.SparkThreshold + " pulls");
            Console.WriteLine(
                "Players   : " + report.Players + "   Pull budget: " + report.Pulls
                + "   Attune budget: " + report.AttuneMaxPulls + "   Seed: " + report.Seed);

            if (report.HypotheticalR5Count > 0)
            {
                Console.WriteLine(
                    "SYNTHETIC : " + report.HypotheticalR5Count + " hypothetical R5 line(s) added to the pool"
                    + " (--hypothetical-r5) — every R5 number below is measured against a line that does"
                    + " not exist in content yet.");
            }

            if (report.EmptyTierWarnings.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("EMPTY TIER WARNING:");
                foreach (string warning in report.EmptyTierWarnings)
                {
                    Console.WriteLine("  " + warning);
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                "Guarantee check: worst observed gap without R" + report.Rules.PityTier + "+ was "
                + report.MaxObservedPullsWithoutPityTier + " pull(s) (hard pity bounds it at "
                + report.Rules.HardPityPull + "); without R" + report.Rules.FloorTier + "+ was "
                + report.MaxObservedPullsWithoutFloorTier + " pull(s) (floor bounds it at "
                + (report.Rules.FloorWindowPulls + 1) + ").");
        }

        private static void PrintSummonMilestones(SummonReport report)
        {
            Console.WriteLine("Pulls to first milestone (mean, median, p90, worst; over players who reached it):");
            PrintPullCountStatLine("R4+", report.FirstR4Plus);
            PrintPullCountStatLine("R4 exactly", report.FirstR4Exact);
            PrintPullCountStatLine("R5", report.FirstR5);
        }

        private static void PrintSummonPerLine(SummonReport report)
        {
            Console.WriteLine("Pulls to first copy, per R4/R5 line (mean, median, p90, worst, never, share still missing at spark):");
            foreach (LineFirstPullStat line in report.PerLineFirstPull)
            {
                string label = "R" + line.Rarity + " " + line.LineId + (line.IsHypothetical ? " (hypothetical)" : "");
                Console.WriteLine("  " + label);
                Console.Write("    ");
                PrintPullCountStatInline(line.Stat);
                string sparkShare = line.StillMissingAtSparkBasisPoints.HasValue
                    ? FormatBasisPointsPercent(line.StillMissingAtSparkBasisPoints.Value)
                    : "n/a (pull budget < spark threshold)";
                Console.WriteLine("    still missing at spark: " + sparkShare);
            }
        }

        private static void PrintSummonDuplicates(SummonReport report)
        {
            Console.WriteLine("Duplicate rate by tier (pulls, duplicates, rate):");
            foreach (TierDuplicateStat tier in report.DuplicatesPerTier)
            {
                Console.WriteLine(
                    "  R" + tier.Rarity + "  pulls " + tier.TotalPulls.ToString().PadLeft(8)
                    + "  duplicates " + tier.Duplicates.ToString().PadLeft(8)
                    + "  " + FormatBasisPointsPercent(tier.DuplicateRateBasisPoints));
            }

            Console.WriteLine();
            Console.WriteLine("Duplicate rate by line (pulls, duplicates, rate):");
            foreach (LineDuplicateStat line in report.DuplicatesPerLine)
            {
                Console.WriteLine(
                    "  R" + line.Rarity + " " + line.LineId.PadRight(28)
                    + "pulls " + line.TotalPulls.ToString().PadLeft(6)
                    + "  duplicates " + line.Duplicates.ToString().PadLeft(6)
                    + "  " + FormatBasisPointsPercent(line.DuplicateRateBasisPoints));
            }
        }

        private static void PrintSummonAttune(SummonReport report)
        {
            Console.WriteLine(
                "Pulls until a line reaches the Attune cap (budget " + report.AttuneMaxPulls + " pulls):");
            Console.Write("  any tier  ");
            PrintPullCountStatInline(report.FirstAttuneCapAnyLine);
            Console.WriteLine();

            var tiers = new List<int>(report.FirstAttuneCapByTier.Keys);
            tiers.Sort();
            foreach (int tier in tiers)
            {
                Console.Write("  R" + tier + "        ");
                PrintPullCountStatInline(report.FirstAttuneCapByTier[tier]);
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Runs both <see cref="SummonRules"/> presets on the same seed/players/pulls and prints a
        /// compact side-by-side table of the rows the doc wants to quote when comparing our
        /// proposed rates against Genshin Impact's — measured with one command, not argued.
        /// </summary>
        private static int RunSummonCompare(ContentPack pack, Options options)
        {
            SummonReport proposed = SummonSimulator.Run(pack, BuildSummonRequest(options, SummonRules.Proposed));
            SummonReport genshin = SummonSimulator.Run(pack, BuildSummonRequest(options, SummonRules.GenshinLike));

            Console.WriteLine("Summon comparison (content " + proposed.ContentVersion + ")");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine(
                "NOT LOCKED — Proposed is DarkMyst.Sim.SummonRules.Proposed (docs/12-summon-spec.md),"
                + " not locked pending phase-C farm data. GenshinLike is Genshin Impact's public wish"
                + " structure expressed in this model's shape, for comparison only — not a DarkMyst"
                + " proposal.");
            Console.WriteLine(
                "Players   : " + proposed.Players + "   Pull budget: " + proposed.Pulls
                + "   Attune budget: " + proposed.AttuneMaxPulls + "   Seed: " + proposed.Seed);
            if (proposed.HypotheticalR5Count > 0)
            {
                Console.WriteLine(
                    "SYNTHETIC : " + proposed.HypotheticalR5Count + " hypothetical R5 line(s) added to the"
                    + " pool (--hypothetical-r5) for both presets.");
            }

            Console.WriteLine();
            PrintCompareRow("", "Proposed", "GenshinLike");
            PrintCompareRow(
                "Base rates",
                FormatBasisPointsPercent(proposed.Rules.R5RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(proposed.Rules.R4RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(proposed.Rules.R3RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(proposed.Rules.R2RateBasisPoints),
                FormatBasisPointsPercent(genshin.Rules.R5RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(genshin.Rules.R4RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(genshin.Rules.R3RateBasisPoints) + "/"
                    + FormatBasisPointsPercent(genshin.Rules.R2RateBasisPoints));
            PrintCompareRow(
                "Pity (R" + proposed.Rules.PityTier + "+/R" + genshin.Rules.PityTier + "+)",
                "soft " + proposed.Rules.SoftPityStartPull + " hard " + proposed.Rules.HardPityPull,
                "soft " + genshin.Rules.SoftPityStartPull + " hard " + genshin.Rules.HardPityPull);
            PrintCompareRow(
                "Floor (R" + proposed.Rules.FloorTier + "+/R" + genshin.Rules.FloorTier + "+)",
                "every " + (proposed.Rules.FloorWindowPulls + 1),
                "every " + (genshin.Rules.FloorWindowPulls + 1));
            Console.WriteLine();

            PrintCompareRow("First R4+ (mean)", FormatMean(proposed.FirstR4Plus), FormatMean(genshin.FirstR4Plus));
            PrintCompareRow("First R4 (mean)", FormatMean(proposed.FirstR4Exact), FormatMean(genshin.FirstR4Exact));
            PrintCompareRow("First R5 (mean)", FormatMean(proposed.FirstR5), FormatMean(genshin.FirstR5));
            Console.WriteLine();

            Console.WriteLine("Still missing at spark, per line:");
            foreach (LineFirstPullStat line in proposed.PerLineFirstPull)
            {
                LineFirstPullStat other = genshin.PerLineFirstPull.Find(l => l.LineId == line.LineId);
                PrintCompareRow(
                    "  R" + line.Rarity + " " + line.LineId,
                    FormatSparkShare(line.StillMissingAtSparkBasisPoints),
                    other == null ? "n/a" : FormatSparkShare(other.StillMissingAtSparkBasisPoints));
            }

            Console.WriteLine();
            Console.WriteLine("Duplicate rate by tier:");
            for (int tier = 5; tier >= 2; tier--)
            {
                TierDuplicateStat left = proposed.DuplicatesPerTier.Find(t => t.Rarity == tier);
                TierDuplicateStat right = genshin.DuplicatesPerTier.Find(t => t.Rarity == tier);
                if (left == null && right == null)
                {
                    continue;
                }

                PrintCompareRow(
                    "  R" + tier,
                    left == null ? "n/a" : FormatBasisPointsPercent(left.DuplicateRateBasisPoints),
                    right == null ? "n/a" : FormatBasisPointsPercent(right.DuplicateRateBasisPoints));
            }

            Console.WriteLine();
            Console.WriteLine("Attune cap by tier (mean pulls, budget " + proposed.AttuneMaxPulls + "):");
            for (int tier = 5; tier >= 2; tier--)
            {
                bool leftHas = proposed.FirstAttuneCapByTier.TryGetValue(tier, out PullCountStat left);
                bool rightHas = genshin.FirstAttuneCapByTier.TryGetValue(tier, out PullCountStat right);
                if (!leftHas && !rightHas)
                {
                    continue;
                }

                PrintCompareRow(
                    "  R" + tier,
                    leftHas ? FormatMean(left) : "n/a",
                    rightHas ? FormatMean(right) : "n/a");
            }

            return 0;
        }

        private static void PrintCompareRow(string label, string left, string right)
        {
            Console.WriteLine(label.PadRight(28) + left.PadRight(24) + right);
        }

        private static string FormatMean(PullCountStat stat)
        {
            return stat.ReachedCount == 0 ? "never" : FormatTenths(stat.MeanTimes10) + " pulls";
        }

        private static string FormatSparkShare(int? basisPoints)
        {
            return basisPoints.HasValue ? FormatBasisPointsPercent(basisPoints.Value) : "n/a";
        }

        private static void PrintPullCountStatLine(string label, PullCountStat stat)
        {
            Console.Write("  " + label.PadRight(12));
            PrintPullCountStatInline(stat);
            Console.WriteLine();
        }

        private static void PrintPullCountStatInline(PullCountStat stat)
        {
            if (stat.ReachedCount == 0)
            {
                Console.Write("no player reached this within budget (0/" + stat.Players + ")");
                return;
            }

            Console.Write(
                "mean " + FormatTenths(stat.MeanTimes10).PadLeft(6)
                + "  median " + stat.Median.ToString().PadLeft(4)
                + "  p90 " + stat.P90.ToString().PadLeft(4)
                + "  worst " + stat.Worst.ToString().PadLeft(4)
                + "  never " + stat.NeverReachedCount + "/" + stat.Players);
        }

        /// <summary>Formats a basis-point value (0..10000, 1bp = 0.01%) as a one-decimal percent
        /// string using only integer division — no floating point in the sim, only in how a
        /// count gets displayed.</summary>
        private static string FormatBasisPointsPercent(int basisPoints)
        {
            int tenths = basisPoints / 10;
            return FormatTenths(tenths) + "%";
        }

        /// <summary>Formats a value already scaled by 10 (e.g. a mean pull count times 10) as a
        /// one-decimal string using only integer division.</summary>
        private static string FormatTenths(int valueTimesTen)
        {
            return (valueTimesTen / 10) + "." + (valueTimesTen % 10);
        }

        // ------------------------------------------------------------------
        // matrix
        // ------------------------------------------------------------------

        /// <summary>One roster × opponent result, before it is turned into a percentage.</summary>
        private sealed class MatrixCell
        {
            public int Wins;
            public int Draws;
            public int Total;
            public long RoundsSum;

            /// <summary>Attacker's remaining HP share (per-mille) summed over winning battles
            /// only — how comfortable the win was, not just whether it happened. A roster that
            /// wins everything at 95%+ HP remaining is a stronger dominance signal than one that
            /// wins everything by a sliver.</summary>
            public long WinningHpShareSum;

            public double WinRatePercent => Total == 0 ? 0.0 : Wins * 100.0 / Total;

            public double AvgRounds => Total == 0 ? 0.0 : RoundsSum / (double)Total;

            public double AvgWinningHpSharePercent => Wins == 0 ? 0.0 : WinningHpShareSum / (double)Wins / 10.0;
        }

        /// <summary>
        /// Runs every named roster against every named opponent (an encounter, or another
        /// roster in <c>--mode mirror</c>) for <see cref="Options.Repeat"/> seeds each, and
        /// prints a roster × opponent table. This is deliberately a general-purpose tool rather
        /// than a fixed report: which rosters and which opponents make up "the roster diversity
        /// question" changes every balance pass, so the shape of the comparison has to be an
        /// argument, not a constant baked into the tool.
        /// </summary>
        private static int RunMatrix(ContentPack pack, Options options)
        {
            List<KeyValuePair<string, List<string>>> rosters = ParseRosters(options);
            if (rosters.Count == 0)
            {
                throw new ArgumentException(
                    "matrix needs at least one roster: pass --rosters \"name=id,id,id,id,id\" "
                    + "(semicolon-separate several) and/or --rosters-file.");
            }

            bool mirror = options.MatrixMode == "mirror";
            List<string> opponentIds = mirror
                ? RosterNames(rosters)
                : ResolveEncounterIds(pack, options);

            var cells = new Dictionary<string, Dictionary<string, MatrixCell>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<string>> roster in rosters)
            {
                var row = new Dictionary<string, MatrixCell>(StringComparer.Ordinal);
                foreach (string opponentId in opponentIds)
                {
                    row[opponentId] = RunMatrixCell(pack, options, rosters, roster.Value, opponentId, mirror);
                }

                cells[roster.Key] = row;
            }

            Console.WriteLine(
                "Matrix     : " + rosters.Count + " roster(s) × " + opponentIds.Count
                + (mirror ? " roster(s) (mirror)" : " encounter(s)")
                + ", " + options.Repeat + " seed(s) each (seeds " + options.Seed + "..+" + (options.Repeat - 1)
                + "), level " + options.Level);
            Console.WriteLine();

            PrintMatrixTable(
                "Win rate", RosterNames(rosters), opponentIds, cells, cell => cell.WinRatePercent, "0.0", "%");
            Console.WriteLine();
            PrintMatrixTable(
                "Avg attacker HP share remaining on a win", RosterNames(rosters), opponentIds, cells,
                cell => cell.AvgWinningHpSharePercent, "0.0", "%");
            Console.WriteLine();

            PrintDominanceChecks(rosters, opponentIds, cells);

            return 0;
        }

        private static MatrixCell RunMatrixCell(
            ContentPack pack,
            Options options,
            List<KeyValuePair<string, List<string>>> rosters,
            List<string> attackerIds,
            string opponentId,
            bool mirror)
        {
            var cell = new MatrixCell();
            TeamDefinition attacker = BuildTeamFromIds(pack, attackerIds, options.Level);

            for (int i = 0; i < options.Repeat; i++)
            {
                ulong seed = options.Seed + (ulong)i;
                TeamDefinition defender = mirror
                    ? BuildTeamFromIds(pack, FindRoster(rosters, opponentId), options.Level)
                    : Progression.BuildEncounterTeam(pack, opponentId);

                BattleResult result = BattleSimulator.Run(new BattleRequest
                {
                    Seed = seed,
                    ContentVersion = pack.Version,
                    Attacker = attacker,
                    Defender = defender
                });

                cell.Total++;
                cell.RoundsSum += result.Rounds;

                if (result.Outcome == BattleOutcome.AttackerVictory)
                {
                    cell.Wins++;
                    cell.WinningHpShareSum += AttackerHpSharePerMille(result);
                }
                else if (result.Outcome == BattleOutcome.Draw)
                {
                    cell.Draws++;
                }
            }

            return cell;
        }

        private static long AttackerHpSharePerMille(BattleResult result)
        {
            long hp = 0;
            long maxHp = 0;
            foreach (UnitSnapshot unit in result.FinalUnits)
            {
                if (unit.Ref.Side != TeamSide.Attacker)
                {
                    continue;
                }

                hp += unit.Hp;
                maxHp += unit.MaxHp;
            }

            return maxHp == 0 ? 0 : hp * 1000L / maxHp;
        }

        private static void PrintMatrixTable(
            string title,
            List<string> rosterNames,
            List<string> columnIds,
            Dictionary<string, Dictionary<string, MatrixCell>> cells,
            Func<MatrixCell, double> value,
            string format,
            string suffix)
        {
            const int nameWidth = 22;
            const int colWidth = 12;

            Console.WriteLine(title + ":");

            var header = new System.Text.StringBuilder("  ".PadRight(nameWidth));
            foreach (string columnId in columnIds)
            {
                header.Append(Truncate(columnId, colWidth - 1).PadLeft(colWidth));
            }

            Console.WriteLine(header.ToString());

            foreach (string rosterName in rosterNames)
            {
                var line = new System.Text.StringBuilder(Truncate(rosterName, nameWidth - 1).PadRight(nameWidth));
                foreach (string columnId in columnIds)
                {
                    double v = value(cells[rosterName][columnId]);
                    line.Append((v.ToString(format, CultureInfo.InvariantCulture) + suffix).PadLeft(colWidth));
                }

                Console.WriteLine(line.ToString());
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
        }

        /// <summary>
        /// Flags the two shapes a balancer actually cares about: a roster that wins essentially
        /// everything (a candidate for "nothing beats this, tune it down") and a roster that
        /// wins essentially nothing (either intentionally bad reference content, or a roster
        /// with a real hole — a human still has to tell the two apart, this just points at them).
        /// </summary>
        private static void PrintDominanceChecks(
            List<KeyValuePair<string, List<string>>> rosters,
            List<string> opponentIds,
            Dictionary<string, Dictionary<string, MatrixCell>> cells)
        {
            const double dominantFloor = 90.0;
            const double weakCeiling = 10.0;

            Console.WriteLine("Dominance check (min win rate across all opponents >= "
                + dominantFloor.ToString("0", CultureInfo.InvariantCulture) + "%):");
            bool anyDominant = false;
            foreach (KeyValuePair<string, List<string>> roster in rosters)
            {
                double min = MinAcross(cells[roster.Key], opponentIds);
                if (min >= dominantFloor)
                {
                    Console.WriteLine(
                        "  " + roster.Key + " never drops below " + min.ToString("0.0", CultureInfo.InvariantCulture)
                        + "% — check whether it should lose somewhere in this set.");
                    anyDominant = true;
                }
            }

            if (!anyDominant)
            {
                Console.WriteLine("  none");
            }

            Console.WriteLine();
            Console.WriteLine("Weak-everywhere check (max win rate across all opponents <= "
                + weakCeiling.ToString("0", CultureInfo.InvariantCulture) + "%):");
            bool anyWeak = false;
            foreach (KeyValuePair<string, List<string>> roster in rosters)
            {
                double max = MaxAcross(cells[roster.Key], opponentIds);
                if (max <= weakCeiling)
                {
                    Console.WriteLine(
                        "  " + roster.Key + " never rises above " + max.ToString("0.0", CultureInfo.InvariantCulture)
                        + "% in this set.");
                    anyWeak = true;
                }
            }

            if (!anyWeak)
            {
                Console.WriteLine("  none");
            }
        }

        private static double MinAcross(Dictionary<string, MatrixCell> row, List<string> opponentIds)
        {
            double min = double.MaxValue;
            foreach (string opponentId in opponentIds)
            {
                double v = row[opponentId].WinRatePercent;
                if (v < min)
                {
                    min = v;
                }
            }

            return min;
        }

        private static double MaxAcross(Dictionary<string, MatrixCell> row, List<string> opponentIds)
        {
            double max = double.MinValue;
            foreach (string opponentId in opponentIds)
            {
                double v = row[opponentId].WinRatePercent;
                if (v > max)
                {
                    max = v;
                }
            }

            return max;
        }

        private static List<string> RosterNames(List<KeyValuePair<string, List<string>>> rosters)
        {
            var names = new List<string>();
            foreach (KeyValuePair<string, List<string>> roster in rosters)
            {
                names.Add(roster.Key);
            }

            return names;
        }

        private static List<string> FindRoster(List<KeyValuePair<string, List<string>>> rosters, string name)
        {
            foreach (KeyValuePair<string, List<string>> roster in rosters)
            {
                if (roster.Key == name)
                {
                    return roster.Value;
                }
            }

            throw new ArgumentException("Unknown roster '" + name + "'.");
        }

        /// <summary>Every encounter in the pack, sorted for stable output, unless the caller
        /// named a subset with --encounters.</summary>
        private static List<string> ResolveEncounterIds(ContentPack pack, Options options)
        {
            if (options.MatrixEncounterIds != null)
            {
                return options.MatrixEncounterIds;
            }

            var ids = new List<string>();
            foreach (EncounterData encounter in pack.Encounters)
            {
                ids.Add(encounter.Id);
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>
        /// Reads --rosters (semicolon-separated <c>name=id,id,id,id,id</c> entries) and
        /// --rosters-file (one such entry per non-empty, non-<c>#</c>-comment line), in that
        /// order, and rejects a duplicate roster name outright rather than silently letting the
        /// second one win — a balance report that quietly used the wrong roster is worse than
        /// one that refuses to run.
        /// </summary>
        private static List<KeyValuePair<string, List<string>>> ParseRosters(Options options)
        {
            var rosters = new List<KeyValuePair<string, List<string>>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddEntry(string entry)
            {
                entry = entry.Trim();
                if (entry.Length == 0)
                {
                    return;
                }

                int eq = entry.IndexOf('=');
                if (eq <= 0 || eq == entry.Length - 1)
                {
                    throw new ArgumentException(
                        "Roster entry '" + entry + "' is not in the form name=id,id,id,id,id.");
                }

                string name = entry.Substring(0, eq).Trim();
                var ids = new List<string>(entry.Substring(eq + 1).Split(','));
                for (int i = 0; i < ids.Count; i++)
                {
                    ids[i] = ids[i].Trim();
                }

                if (!seen.Add(name))
                {
                    throw new ArgumentException("Roster name '" + name + "' is defined twice.");
                }

                rosters.Add(new KeyValuePair<string, List<string>>(name, ids));
            }

            if (options.MatrixRostersFile != null)
            {
                foreach (string line in File.ReadAllLines(options.MatrixRostersFile))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    AddEntry(trimmed);
                }
            }

            foreach (string group in options.MatrixRosters)
            {
                foreach (string entry in group.Split(';'))
                {
                    AddEntry(entry);
                }
            }

            return rosters;
        }

        /// <summary>Builds a battle-ready team from bare character ids at a uniform level, all in
        /// slots 0..n-1 — the same construction <see cref="BuildRoster"/> uses for the default
        /// sim roster, generalised to an arbitrary named roster.</summary>
        private static TeamDefinition BuildTeamFromIds(ContentPack pack, List<string> characterIds, int level)
        {
            var placements = new List<KeyValuePair<int, OwnedCharacter>>();
            for (int slot = 0; slot < characterIds.Count && slot < Formation.SlotCount; slot++)
            {
                placements.Add(new KeyValuePair<int, OwnedCharacter>(slot, new OwnedCharacter
                {
                    InstanceId = "matrix_" + slot,
                    OwnerId = "matrix",
                    CharacterId = characterIds[slot],
                    Level = level,
                    ContentVersion = pack.Version
                }));
            }

            return Progression.BuildTeam(pack, "matrix_roster", 0, placements);
        }

        /// <summary>
        /// Rest if the team is hurting and a Rest node happens to be on offer; otherwise take the
        /// first option. Simple and fully deterministic — the difficulty curve this is meant to
        /// reveal comes from the content's numbers, not from clever play.
        /// </summary>
        private static int AutoPick(ExpeditionRun run, IReadOnlyList<ExpeditionChoice> choices)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Kind == NodeKind.Rest && TeamHpFractionPerMille(run) < 500)
                {
                    return i;
                }
            }

            return 0;
        }

        private static int TeamHpFractionPerMille(ExpeditionRun run)
        {
            long hp = 0;
            long maxHp = 0;
            foreach (RunTeamMemberState member in run.State.Team)
            {
                hp += member.CurrentHp;
                maxHp += member.MaxHp;
            }

            return maxHp == 0 ? 0 : (int)(hp * 1000L / maxHp);
        }

        private static string DescribeOutcome(ExpeditionRun run, ExpeditionNodeOutcome outcome)
        {
            ExpeditionNodeState node = run.State.Nodes[outcome.NodeId];
            string header = "Node " + node.Id.ToString().PadLeft(2) + " [layer " + node.Layer + "] "
                + outcome.Kind + (string.IsNullOrEmpty(outcome.RefId) ? "" : " (" + outcome.RefId + ")");

            var lines = new List<string> { header };

            if (outcome.BattleResult != null)
            {
                lines.Add(
                    "  -> " + outcome.BattleResult.Outcome + " after " + outcome.BattleResult.Rounds
                    + " round(s), checksum " + outcome.BattleResult.Checksum);
            }

            if (!string.IsNullOrEmpty(outcome.GrantedBuffSkillId))
            {
                lines.Add("  -> buff granted: " + outcome.GrantedBuffSkillId);
            }

            if (outcome.HealedPerMille.HasValue)
            {
                lines.Add("  -> healed " + (outcome.HealedPerMille.Value / 10.0) + "% of missing HP");
            }

            foreach (GrantedReward reward in outcome.Rewards)
            {
                lines.Add("  -> reward: " + reward.Kind + (reward.RefId == null ? "" : " " + reward.RefId)
                    + " x" + reward.Amount);
            }

            if (outcome.RunEnded)
            {
                lines.Add("  == run ended: " + outcome.RunStatusAfter + " ==");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string DescribeMaterials(ExpeditionRun run)
        {
            if (run.State.BankedMaterials.Count == 0)
            {
                return "(none)";
            }

            var parts = new List<string>();
            foreach (MaterialStack stack in run.State.BankedMaterials)
            {
                parts.Add(stack.MaterialId + " x" + stack.Amount);
            }

            return string.Join(", ", parts);
        }

        private static BattleResult Simulate(ContentPack pack, Options options, ulong seed)
        {
            return BattleSimulator.Run(new BattleRequest
            {
                Seed = seed,
                ContentVersion = pack.Version,
                Attacker = BuildRoster(pack, options),
                Defender = Progression.BuildEncounterTeam(pack, options.EncounterId)
            });
        }

        /// <summary>
        /// Builds a stand-in player team. Real battles get their roster from the account
        /// service; the runner only needs something representative to balance against.
        /// </summary>
        private static TeamDefinition BuildRoster(ContentPack pack, Options options)
        {
            return Progression.BuildTeam(pack, "sim_roster", 0, BuildPlacements(pack, options));
        }

        /// <summary>Same stand-in roster as <see cref="BuildRoster"/>, as placements rather than
        /// a built team — what <c>ExpeditionRun.Start</c> takes instead of a <c>TeamDefinition</c>.</summary>
        private static List<KeyValuePair<int, OwnedCharacter>> BuildPlacements(ContentPack pack, Options options)
        {
            var placements = new List<KeyValuePair<int, OwnedCharacter>>();
            for (int slot = 0; slot < options.Roster.Count && slot < Formation.SlotCount; slot++)
            {
                placements.Add(new KeyValuePair<int, OwnedCharacter>(slot, new OwnedCharacter
                {
                    InstanceId = "sim_" + slot,
                    OwnerId = "sim",
                    CharacterId = options.Roster[slot],
                    Level = options.Level,
                    ContentVersion = pack.Version
                }));
            }

            return placements;
        }

        private static string Describe(BattleEvent evt)
        {
            string indent = evt.Round > 0 ? "  " : "";
            return indent + evt;
        }

        private static string Percent(int value, int total)
        {
            return total == 0
                ? "-"
                : (value * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"simrunner <command> [options]

Commands
  validate                 Load the content pack and report every problem found.
  battle                   Run one battle and print its event log.
  sweep                    Run many seeds and report win rate, length and survival.
  expedition               Auto-play one expedition stage, or sweep many seeds of it.
  matrix                   Run named rosters against encounters (or each other) and print a table.
  roster                   Print the content pack's affinity/role/rarity composition.
  summon                   Simulate the character summon system and report pity/duplicate/Attune stats.

Options
  --content <dir>          Content directory (default: ./content)
  --encounter <id>         Encounter to fight (default: enc_tutorial_hounds)
  --stage <id>             Expedition stage to play (default: stg_ashfields)
  --seed <n>               Starting RNG seed (default: 1)
  --repeat <n>             Battles/runs to sweep (default: 200 for sweep, 1 for expedition, 300 for matrix)
  --level <n>              Level for every simulated roster character (default: 20)
  --roster <a,b,c,d,e>     Character ids for slots 0-4 (battle/sweep)
  --rosters <n=a,b,c,d,e>  Named roster for matrix; ';'-separate several, or repeat the flag
  --rosters-file <path>    File of 'name=a,b,c,d,e' lines (one roster per line, '#' comments)
  --encounters <a,b,c>     Encounters matrix tests against (default: every encounter in the pack)
  --mode <encounters|mirror>  matrix opponents: encounters (default) or the other rosters
  --players <n>            summon: players to simulate (default: 10000)
  --pulls <n>              summon: pull budget per player, all sections but Attune (default: 300)
  --attune-max-pulls <n>   summon: separate, larger pull budget for the Attune section (default: 5000)
  --hypothetical-r5 <n>    summon: add N synthetic R5 lines so the R5 row can be measured before one is authored

Examples
  simrunner validate
  simrunner roster
  simrunner battle --encounter enc_boss_ashen_revenant --seed 20260920
  simrunner sweep  --encounter enc_crypt_patrol --repeat 500
  simrunner expedition --stage stg_ashfields --seed 20260920
  simrunner expedition --stage stg_ashfields --repeat 200 --level 12
  simrunner matrix --level 12 --repeat 300 \
    --rosters ""balanced=chr_ashen_knight_i,chr_grave_warden_i,chr_ember_adept_i,chr_tide_oracle_i,chr_pale_stalker_i;no_healer=chr_ashen_knight_i,chr_grave_warden_i,chr_thorn_maiden_i,chr_mire_hexer_i,chr_pale_stalker_i""
  simrunner summon --seed 20260920
  simrunner summon --seed 20260920 --hypothetical-r5 1");
        }

        private sealed class Options
        {
            public string Command = "validate";
            public string ContentDirectory = "content";
            public string EncounterId = "enc_tutorial_hounds";
            public string StageId = "stg_ashfields";
            public ulong Seed = 1;

            /// <summary>-1 means "not passed on the command line": <see cref="Parse"/> then picks
            /// the default for whichever command is running (200 for sweep, 1 for expedition).</summary>
            public int Repeat = -1;

            public int Level = 20;

            public List<string> Roster = new List<string>
            {
                "chr_ashen_knight_i",
                "chr_grave_warden_i",
                "chr_ember_adept_i",
                "chr_tide_oracle_i",
                "chr_pale_stalker_i"
            };

            /// <summary>matrix: raw "name=id,id,id,id,id" groups, one per --rosters flag
            /// (each may itself hold several ';'-separated entries).</summary>
            public List<string> MatrixRosters = new List<string>();

            /// <summary>matrix: optional file of "name=id,id,id,id,id" lines, for roster catalogs
            /// too long to comfortably pass as one command-line argument.</summary>
            public string MatrixRostersFile;

            /// <summary>matrix: encounters to test against. Null means "every encounter in the
            /// pack", resolved once the pack is loaded.</summary>
            public List<string> MatrixEncounterIds;

            /// <summary>matrix: "encounters" (default, roster vs. each named encounter) or
            /// "mirror" (roster vs. every other named roster).</summary>
            public string MatrixMode = "encounters";

            /// <summary>summon: players to simulate.</summary>
            public int SummonPlayers = 10000;

            /// <summary>summon: pull budget per player for every section except Attune.</summary>
            public int SummonPulls = 300;

            /// <summary>summon: separate, larger pull budget for the Attune section.</summary>
            public int SummonAttuneMaxPulls = 5000;

            /// <summary>summon: synthetic rarity-5 stage-I lines added to the pool.</summary>
            public int SummonHypotheticalR5 = 0;

            /// <summary>summon: which <c>SummonRules</c> preset to run — "proposed" (default) or
            /// "genshin-like".</summary>
            public string SummonRulesPreset = "proposed";

            /// <summary>summon: run both presets on the same seed/players/pulls and print a
            /// side-by-side table instead of a single report.</summary>
            public bool SummonCompare = false;

            public static Options Parse(string[] args)
            {
                var options = new Options { Command = args[0] };

                for (int i = 1; i < args.Length; i++)
                {
                    string key = args[i];
                    string value = i + 1 < args.Length ? args[i + 1] : null;

                    switch (key)
                    {
                        case "--content":
                            options.ContentDirectory = Require(key, value);
                            i++;
                            break;
                        case "--encounter":
                            options.EncounterId = Require(key, value);
                            i++;
                            break;
                        case "--stage":
                            options.StageId = Require(key, value);
                            i++;
                            break;
                        case "--seed":
                            options.Seed = ulong.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--repeat":
                            options.Repeat = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--level":
                            options.Level = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--roster":
                            options.Roster = new List<string>(Require(key, value).Split(','));
                            i++;
                            break;
                        case "--rosters":
                            options.MatrixRosters.Add(Require(key, value));
                            i++;
                            break;
                        case "--rosters-file":
                            options.MatrixRostersFile = Require(key, value);
                            i++;
                            break;
                        case "--encounters":
                            options.MatrixEncounterIds = new List<string>(Require(key, value).Split(','));
                            i++;
                            break;
                        case "--mode":
                            options.MatrixMode = Require(key, value);
                            i++;
                            break;
                        case "--players":
                            options.SummonPlayers = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--pulls":
                            options.SummonPulls = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--attune-max-pulls":
                            options.SummonAttuneMaxPulls = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--hypothetical-r5":
                            options.SummonHypotheticalR5 = int.Parse(Require(key, value), CultureInfo.InvariantCulture);
                            i++;
                            break;
                        case "--rules":
                            options.SummonRulesPreset = Require(key, value).ToLowerInvariant();
                            i++;
                            break;
                        case "--compare":
                            options.SummonCompare = true;
                            break;
                        default:
                            throw new ArgumentException("Unknown option '" + key + "'.");
                    }
                }

                if (options.MatrixMode != "encounters" && options.MatrixMode != "mirror")
                {
                    throw new ArgumentException("--mode must be 'encounters' or 'mirror' (got '"
                        + options.MatrixMode + "').");
                }

                if (options.SummonRulesPreset != "proposed" && options.SummonRulesPreset != "genshin-like")
                {
                    throw new ArgumentException("--rules must be 'proposed' or 'genshin-like' (got '"
                        + options.SummonRulesPreset + "').");
                }

                if (options.Repeat < 0)
                {
                    options.Repeat = options.Command == "expedition" ? 1 : (options.Command == "matrix" ? 300 : 200);
                }

                if (!Directory.Exists(options.ContentDirectory))
                {
                    throw new ContentException(
                        "Content directory '" + options.ContentDirectory
                        + "' not found. Run from the repository root or pass --content.");
                }

                return options;
            }

            private static string Require(string key, string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException(key + " needs a value.");
                }

                return value;
            }
        }
    }
}
