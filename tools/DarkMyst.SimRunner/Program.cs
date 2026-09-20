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
    /// there is a client to look at. Three modes:
    /// <list type="bullet">
    /// <item><description><c>validate</c> — load a content pack and report every problem.</description></item>
    /// <item><description><c>battle</c> — run one fight and print the event log.</description></item>
    /// <item><description><c>sweep</c> — run many seeds and report the win rate and length.</description></item>
    /// <item><description><c>expedition</c> — auto-play one expedition stage end to end, or
    /// sweep many seeds of it and report the farm loop's shape.</description></item>
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
                        + pack.Encounters.Count + " encounters, " + pack.Skills.Count + " skills.");
                    return 0;

                case "battle":
                    return RunBattle(pack, options);

                case "sweep":
                    return RunSweep(pack, options);

                case "expedition":
                    return RunExpedition(pack, options);

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

Options
  --content <dir>          Content directory (default: ./content)
  --encounter <id>         Encounter to fight (default: enc_tutorial_hounds)
  --stage <id>             Expedition stage to play (default: stg_ashfields)
  --seed <n>               Starting RNG seed (default: 1)
  --repeat <n>             Battles/runs to sweep (default: 200 for sweep, 1 for expedition)
  --level <n>              Level for every simulated roster character (default: 20)
  --roster <a,b,c,d,e>     Character ids for slots 0-4

Examples
  simrunner validate
  simrunner battle --encounter enc_boss_ashen_revenant --seed 20260920
  simrunner sweep  --encounter enc_crypt_patrol --repeat 500
  simrunner expedition --stage stg_ashfields --seed 20260920
  simrunner expedition --stage stg_ashfields --repeat 200 --level 12");
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
                        default:
                            throw new ArgumentException("Unknown option '" + key + "'.");
                    }
                }

                if (options.Repeat < 0)
                {
                    options.Repeat = options.Command == "expedition" ? 1 : 200;
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
