using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using DarkMyst.Expedition;
using DarkMyst.Expedition.Model;

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

        private static int RunSweep(ContentPack pack, Options options)
        {
            int wins = 0;
            int draws = 0;
            long totalRounds = 0;
            var survivors = new Dictionary<string, int>();

            for (int i = 0; i < options.Repeat; i++)
            {
                BattleResult result = Simulate(pack, options, options.Seed + (ulong)i);
                totalRounds += result.Rounds;

                if (result.Outcome == BattleOutcome.AttackerVictory)
                {
                    wins++;
                }
                else if (result.Outcome == BattleOutcome.Draw)
                {
                    draws++;
                }

                foreach (UnitSnapshot unit in result.FinalUnits)
                {
                    if (unit.Ref.Side == TeamSide.Attacker && unit.Alive)
                    {
                        survivors.TryGetValue(unit.CharacterId, out int count);
                        survivors[unit.CharacterId] = count + 1;
                    }
                }
            }

            Console.WriteLine("Encounter : " + options.EncounterId);
            Console.WriteLine("Battles   : " + options.Repeat + " (seeds " + options.Seed + "..+"
                              + (options.Repeat - 1) + ")");
            Console.WriteLine("Win rate  : " + Percent(wins, options.Repeat)
                              + "   draws " + Percent(draws, options.Repeat));
            Console.WriteLine("Avg length: " + (totalRounds / (double)options.Repeat).ToString("0.0", CultureInfo.InvariantCulture)
                              + " rounds");
            Console.WriteLine();
            Console.WriteLine("Survival by character:");

            var names = new List<string>(survivors.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string characterId in names)
            {
                Console.WriteLine("  " + characterId.PadRight(28) + Percent(survivors[characterId], options.Repeat));
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

Examples
  simrunner validate
  simrunner battle --encounter enc_boss_ashen_revenant --seed 20260920
  simrunner sweep  --encounter enc_crypt_patrol --repeat 500
  simrunner expedition --stage stg_ashfields --seed 20260920
  simrunner expedition --stage stg_ashfields --repeat 200 --level 12
  simrunner matrix --level 12 --repeat 300 \
    --rosters ""balanced=chr_ashen_knight_i,chr_grave_warden_i,chr_ember_adept_i,chr_tide_oracle_i,chr_pale_stalker_i;no_healer=chr_ashen_knight_i,chr_grave_warden_i,chr_thorn_maiden_i,chr_mire_hexer_i,chr_pale_stalker_i""");
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
                        default:
                            throw new ArgumentException("Unknown option '" + key + "'.");
                    }
                }

                if (options.MatrixMode != "encounters" && options.MatrixMode != "mirror")
                {
                    throw new ArgumentException("--mode must be 'encounters' or 'mirror' (got '"
                        + options.MatrixMode + "').");
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
