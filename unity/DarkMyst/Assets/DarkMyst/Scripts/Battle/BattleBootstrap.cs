using System.Collections;
using System.Collections.Generic;
using DarkMyst.Client.Content;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using UnityEngine;

namespace DarkMyst.Client.Battle
{
    /// <summary>
    /// Smallest end-to-end path through the game: load content, build a team, run a battle in
    /// the shared engine, replay the log. Drop it on an empty scene, press Play, and the
    /// Console shows a real fight.
    /// <para>
    /// It exists so the client-side wiring is exercised from day one instead of being written
    /// at the end against a finished backend. The offline training ground uses exactly this
    /// path; a battle that pays out rewards runs the same engine on the server and the client
    /// only replays what comes back.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(BattleReplay))]
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] private string _encounterId = "enc_tutorial_hounds";
        [SerializeField] private int _rosterLevel = 12;
        [SerializeField] private ulong _seed = 20260920;

        [SerializeField]
        private List<string> _roster = new List<string>
        {
            "chr_ashen_knight_i",
            "chr_grave_warden_i",
            "chr_ember_adept_i",
            "chr_tide_oracle_i",
            "chr_pale_stalker_i"
        };

        private BattleReplay _replay;

        private void Awake()
        {
            _replay = GetComponent<BattleReplay>();
            _replay.OnEvent += evt => Debug.Log(evt.ToString());
            _replay.OnFinished += result =>
                Debug.Log($"Battle over: {result.Outcome} ({result.EndReason}) after {result.Rounds} rounds. "
                          + $"checksum={result.Checksum}");
        }

        private IEnumerator Start()
        {
            yield return StreamingContent.Load(RunBattle, reason => Debug.LogError("Content failed: " + reason));
        }

        private void RunBattle(ContentPack pack)
        {
            var placements = new List<KeyValuePair<int, OwnedCharacter>>();
            for (int slot = 0; slot < _roster.Count && slot < Formation.SlotCount; slot++)
            {
                placements.Add(new KeyValuePair<int, OwnedCharacter>(slot, new OwnedCharacter
                {
                    InstanceId = "local_" + slot,
                    OwnerId = "local",
                    CharacterId = _roster[slot],
                    Level = _rosterLevel,
                    ContentVersion = pack.Version
                }));
            }

            BattleResult result = BattleSimulator.Run(new BattleRequest
            {
                Seed = _seed,
                ContentVersion = pack.Version,
                Attacker = Progression.BuildTeam(pack, "local_team", 0, placements),
                Defender = Progression.BuildEncounterTeam(pack, _encounterId)
            });

            _replay.Play(result);
        }
    }
}
