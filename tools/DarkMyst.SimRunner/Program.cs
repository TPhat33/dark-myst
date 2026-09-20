using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;

namespace DarkMyst.SimRunner
{
    /// <summary>
    /// Runs battles from the command line so combat can be balanced and debugged long before
    /// there is a client to look at. Three modes:
    /// <list type="bullet">
    /// <item><description><c>validate</c> — load a content pack and report every problem.</description></item>
    /// <item><description><c>battle</c> — run one fight and print the event log.</description></item>
    /// <item><description><c>sweep</c> — run many seeds and report the win rate and length.</description></item>
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

            return Progression.BuildTeam(pack, "sim_roster", 0, placements);
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

Options
  --content <dir>          Content directory (default: ./content)
  --encounter <id>         Encounter to fight (default: enc_tutorial_hounds)
  --seed <n>               Starting RNG seed (default: 1)
  --repeat <n>             Battles to run in sweep mode (default: 200)
  --level <n>              Level for every simulated roster character (default: 20)
  --roster <a,b,c,d,e>     Character ids for slots 0-4

Examples
  simrunner validate
  simrunner battle --encounter enc_boss_ashen_revenant --seed 20260920
  simrunner sweep  --encounter enc_crypt_patrol --repeat 500");
        }

        private sealed class Options
        {
            public string Command = "validate";
            public string ContentDirectory = "content";
            public string EncounterId = "enc_tutorial_hounds";
            public ulong Seed = 1;
            public int Repeat = 200;
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
