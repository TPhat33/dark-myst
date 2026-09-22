using System;
using System.Collections.Generic;
using System.Threading;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;

namespace DarkMyst.Sim
{
    /// <summary>
    /// Parameters for one battle sweep — the same shape <c>simrunner sweep</c> has taken from the
    /// command line since docs/07-testing-plan.md, now also reachable from the admin tool's sweep
    /// button (docs/11-admin-spec.md) through this shared library.
    /// </summary>
    public sealed class SweepRequest
    {
        public string EncounterId { get; set; }

        /// <summary>Character ids for slots 0..N-1 (up to <see cref="Formation.SlotCount"/>).</summary>
        public IReadOnlyList<string> Roster { get; set; }

        public int Level { get; set; } = 20;

        public ulong Seed { get; set; } = 1;

        public int Repeat { get; set; } = 200;
    }

    public sealed class SweepSurvivorEntry
    {
        public string CharacterId { get; set; }

        /// <summary>Number of the sweep's battles this character was alive at the end of.</summary>
        public int Survived { get; set; }
    }

    public sealed class SweepResult
    {
        public string EncounterId { get; set; }

        public int Battles { get; set; }

        public int Wins { get; set; }

        public int Draws { get; set; }

        public double AverageRounds { get; set; }

        public List<SweepSurvivorEntry> Survivors { get; set; } = new List<SweepSurvivorEntry>();
    }

    /// <summary>
    /// Runs the exact loop <c>tools/DarkMyst.SimRunner</c>'s <c>sweep</c> command has always run —
    /// many seeds of one attacker roster against one encounter, win rate, average length and
    /// survival by character — extracted into its own small library so the admin tool's sweep
    /// button (docs/11-admin-spec.md) and the CLI both call one implementation, rather than the
    /// CLI's loop being duplicated a second time inside the web server.
    /// <para>
    /// Deliberately does not depend on <c>DarkMyst.Expedition</c>: <c>simrunner sweep</c> never
    /// has either (only its separate <c>expedition</c> command does), and the admin sweep button
    /// is scoped to the same single-battle sweep, not a full farm-run sweep — see
    /// docs/11-admin-spec.md's sweep design note for why that scope was chosen.
    /// </para>
    /// </summary>
    public static class BattleSweepRunner
    {
        /// <summary>
        /// Runs the sweep. <paramref name="ct"/> is checked once per battle so a caller running
        /// this off a web request (the admin sweep button) can stop a long sweep early the moment
        /// the client disconnects or the request is cancelled, instead of a stray sweep spinning
        /// on the thread pool for its whole configured repeat count regardless.
        /// </summary>
        public static SweepResult Run(ContentPack pack, SweepRequest request, CancellationToken ct = default)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrEmpty(request.EncounterId))
            {
                throw new ArgumentException("EncounterId is required.", nameof(request));
            }

            if (request.Roster == null || request.Roster.Count == 0)
            {
                throw new ArgumentException("Roster must have at least one character id.", nameof(request));
            }

            if (request.Repeat <= 0)
            {
                throw new ArgumentException("Repeat must be positive.", nameof(request));
            }

            // Resolved once up front so a bad id fails clearly before any battle runs, rather
            // than partway through a sweep of hundreds.
            pack.GetEncounter(request.EncounterId);
            foreach (string characterId in request.Roster)
            {
                pack.GetCharacter(characterId);
            }

            int wins = 0;
            int draws = 0;
            long totalRounds = 0;
            var survivors = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < request.Repeat; i++)
            {
                ct.ThrowIfCancellationRequested();

                BattleResult result = BattleSimulator.Run(new BattleRequest
                {
                    Seed = request.Seed + (ulong)i,
                    ContentVersion = pack.Version,
                    Attacker = BuildRoster(pack, request.Roster, request.Level),
                    Defender = Progression.BuildEncounterTeam(pack, request.EncounterId)
                });

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

            var survivorList = new List<SweepSurvivorEntry>();
            var names = new List<string>(survivors.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string characterId in names)
            {
                survivorList.Add(new SweepSurvivorEntry { CharacterId = characterId, Survived = survivors[characterId] });
            }

            return new SweepResult
            {
                EncounterId = request.EncounterId,
                Battles = request.Repeat,
                Wins = wins,
                Draws = draws,
                AverageRounds = totalRounds / (double)request.Repeat,
                Survivors = survivorList
            };
        }

        /// <summary>Builds a stand-in player team from bare character ids — the same shape
        /// <c>simrunner</c>'s own roster builder has always built for <c>sweep</c>/<c>battle</c>.</summary>
        private static TeamDefinition BuildRoster(ContentPack pack, IReadOnlyList<string> roster, int level)
        {
            var placements = new List<KeyValuePair<int, OwnedCharacter>>();
            for (int slot = 0; slot < roster.Count && slot < Formation.SlotCount; slot++)
            {
                placements.Add(new KeyValuePair<int, OwnedCharacter>(slot, new OwnedCharacter
                {
                    InstanceId = "sim_" + slot,
                    OwnerId = "sim",
                    CharacterId = roster[slot],
                    Level = level,
                    ContentVersion = pack.Version
                }));
            }

            return Progression.BuildTeam(pack, "sim_roster", 0, placements);
        }
    }
}
