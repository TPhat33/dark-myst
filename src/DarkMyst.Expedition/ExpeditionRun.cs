using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using DarkMyst.Expedition.Model;
using Newtonsoft.Json;

namespace DarkMyst.Expedition
{
    /// <summary>
    /// A single expedition (สำรวจ) run: a node-graph map generated once from a seed, played one
    /// node at a time. The whole class is a state machine over <see cref="ExpeditionRunState"/> —
    /// every field that decides what happens next lives there, in plain serializable data, so a
    /// server can own the state exactly the way it owns a battle.
    /// <para>
    /// See <c>docs/09-expedition-spec.md</c> for the rules this class implements and the reasons
    /// behind every choice the design plan left open: whether HP carries between nodes (yes),
    /// whether a lost battle keeps rewards banked so far (yes, by default, per-stage), and how a
    /// run-scoped buff manages to stop applying the instant the run ends (it is never anything
    /// more than a skill id in <see cref="ExpeditionRunState.RunBuffSkillIds"/>, discarded with
    /// the rest of the run).
    /// </para>
    /// </summary>
    public sealed class ExpeditionRun
    {
        // Distinct, fixed RNG streams so map generation, battle resolution, reward rolls and
        // event rolls never share state — the same reasoning DeterministicRandom's own `stream`
        // parameter exists for. Values are arbitrary but fixed forever: changing one would
        // silently change every run generated after the change.
        private const ulong MapStream = 0x4D41505F53545245UL;
        private const ulong RewardStream = 0x5245574152445F31UL;
        private const ulong EventStream = 0x4556454E545F5F31UL;

        private ExpeditionRun(ExpeditionRunState state)
        {
            State = state;
        }

        /// <summary>
        /// The complete, serializable state of this run. Read freely; the only supported way to
        /// change it is <see cref="Choose"/>.
        /// </summary>
        public ExpeditionRunState State { get; }

        // ------------------------------------------------------------------
        // Start / resume / serialize
        // ------------------------------------------------------------------

        /// <summary>
        /// Generates a brand new run: the whole map, up front, from <paramref name="seed"/>. The
        /// team is snapshotted at this moment (level, focus, inherited bonus) and never re-read
        /// from the account afterwards — see the type-level remarks on
        /// <see cref="RunTeamMemberState"/> for why.
        /// </summary>
        public static ExpeditionRun Start(
            ContentPack pack,
            string stageId,
            ulong seed,
            int leaderSlot,
            IEnumerable<KeyValuePair<int, OwnedCharacter>> placements)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (placements == null)
            {
                throw new ArgumentNullException(nameof(placements));
            }

            StageData stage = pack.GetStage(stageId);

            var state = new ExpeditionRunState
            {
                StageId = stageId,
                Seed = seed,
                ContentVersion = pack.Version,
                RulesVersion = CombatRules.Version,
                LeaderSlot = leaderSlot
            };

            BuildTeamSnapshot(pack, state, leaderSlot, placements);

            var mapRng = new DeterministicRandom(RunKey(state), MapStream);
            ExpeditionMapResult map = ExpeditionMapGenerator.Generate(stage, mapRng);
            state.TotalRngCalls += mapRng.CallCount;
            state.Nodes = map.Nodes;
            state.EntryNodeIds = map.EntryNodeIds;

            return new ExpeditionRun(state);
        }

        /// <summary>
        /// Reconstructs a run from JSON produced by <see cref="Serialize"/>. Refuses to continue
        /// under content or rules other than the ones the run began under — resuming is exactly
        /// as strict as starting fresh would be.
        /// </summary>
        public static ExpeditionRun Resume(ContentPack pack, string json)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentException("json is empty.", nameof(json));
            }

            ExpeditionRunState state;
            try
            {
                state = JsonConvert.DeserializeObject<ExpeditionRunState>(json, ContentPack.SerializerSettings);
            }
            catch (JsonException ex)
            {
                throw new ExpeditionException("Could not read expedition run state: " + ex.Message);
            }

            if (state == null)
            {
                throw new ExpeditionException("Could not read expedition run state.");
            }

            var run = new ExpeditionRun(state);
            run.RequireLiveContent(pack);
            pack.GetStage(state.StageId); // fails fast with a clear message if the stage vanished
            return run;
        }

        /// <summary>Serializes the run's whole state, ready for <see cref="Resume"/> later.</summary>
        public string Serialize()
        {
            return JsonConvert.SerializeObject(State, ContentPack.SerializerSettings);
        }

        // ------------------------------------------------------------------
        // Play
        // ------------------------------------------------------------------

        /// <summary>Nodes reachable from wherever the run currently stands. Empty once the run
        /// has ended (cleared or failed).</summary>
        public IReadOnlyList<ExpeditionChoice> AvailableChoices()
        {
            var choices = new List<ExpeditionChoice>();
            if (State.Status != RunStatus.InProgress)
            {
                return choices;
            }

            List<int> nextIds = State.CurrentNodeId.HasValue
                ? NodeById(State.CurrentNodeId.Value).NextNodeIds
                : State.EntryNodeIds;

            foreach (int id in nextIds)
            {
                ExpeditionNodeState node = NodeById(id);
                choices.Add(new ExpeditionChoice { NodeId = node.Id, Kind = node.Kind });
            }

            return choices;
        }

        /// <summary>
        /// Advances to and resolves the option at <paramref name="choiceIndex"/> in the list
        /// <see cref="AvailableChoices"/> most recently returned, and reports what happened. A
        /// battle or boss node runs the exact same <see cref="BattleSimulator"/> a standalone
        /// fight would, seeded so that anyone who knows the run's seed, stage and this node's id
        /// can recompute the identical <see cref="BattleResult"/> independently.
        /// </summary>
        public ExpeditionNodeOutcome Choose(ContentPack pack, int choiceIndex)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            RequireLiveContent(pack);

            if (State.Status != RunStatus.InProgress)
            {
                throw new ExpeditionException("This run has already ended (" + State.Status + ").");
            }

            IReadOnlyList<ExpeditionChoice> choices = AvailableChoices();
            if (choiceIndex < 0 || choiceIndex >= choices.Count)
            {
                throw new ExpeditionException(
                    "Choice " + choiceIndex + " is not available; there are " + choices.Count + " option(s).");
            }

            ExpeditionNodeState node = NodeById(choices[choiceIndex].NodeId);
            State.CurrentNodeId = node.Id;

            StageData stage = pack.GetStage(State.StageId);
            ExpeditionNodeOutcome outcome = Resolve(pack, stage, node);
            State.Log.Add(ToLogEntry(outcome));

            return outcome;
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        private ExpeditionNodeOutcome Resolve(ContentPack pack, StageData stage, ExpeditionNodeState node)
        {
            switch (node.Kind)
            {
                case NodeKind.Battle:
                case NodeKind.Boss:
                    return ResolveBattle(pack, stage, node);
                case NodeKind.Event:
                    return ResolveEvent(pack, node);
                case NodeKind.Treasure:
                    return ResolveTreasure(pack, node);
                case NodeKind.Rest:
                    return ResolveRest(stage, node);
                default:
                    throw new ExpeditionException("Unhandled node kind " + node.Kind + ".");
            }
        }

        private ExpeditionNodeOutcome ResolveBattle(ContentPack pack, StageData stage, ExpeditionNodeState node)
        {
            TeamDefinition attacker = BuildBattleTeam(pack);
            TeamDefinition defender = Progression.BuildEncounterTeam(pack, node.RefId);

            BattleResult result = BattleSimulator.Run(new BattleRequest
            {
                Seed = NodeSeed(node.Id),
                ContentVersion = pack.Version,
                Attacker = attacker,
                Defender = defender
            });

            State.TotalRngCalls += result.RngCalls;
            ApplyCarriedHp(result);

            var outcome = new ExpeditionNodeOutcome
            {
                NodeId = node.Id,
                Kind = node.Kind,
                RefId = node.RefId,
                BattleResult = result
            };

            // A draw is a loss for the attacker in PvE — the same rule docs/02-combat-spec.md
            // states for a standalone encounter. A run is not a gentler ruleset than a fight.
            if (result.Outcome != BattleOutcome.AttackerVictory)
            {
                FailRun(stage, outcome);
                return outcome;
            }

            string rewardTableId = node.Kind == NodeKind.Boss ? stage.ClearRewardTableId : stage.NodeRewardTableId;
            if (!string.IsNullOrEmpty(rewardTableId))
            {
                GrantedReward reward = RollReward(pack, rewardTableId, node.Id);
                if (reward != null)
                {
                    outcome.Rewards.Add(reward);
                    Bank(reward);
                }
            }

            if (node.Kind == NodeKind.Boss)
            {
                State.Status = RunStatus.Cleared;
                outcome.RunEnded = true;
            }

            outcome.RunStatusAfter = State.Status;
            return outcome;
        }

        private void FailRun(StageData stage, ExpeditionNodeOutcome outcome)
        {
            State.Status = RunStatus.Failed;
            outcome.RunEnded = true;
            outcome.RunStatusAfter = RunStatus.Failed;

            if (!stage.KeepRewardsOnDefeat)
            {
                State.BankedGold = 0;
                State.BankedMaterials.Clear();
                State.BankedCharacterIds.Clear();
            }
        }

        private ExpeditionNodeOutcome ResolveEvent(ContentPack pack, ExpeditionNodeState node)
        {
            EventData evt = pack.GetEvent(node.RefId);
            var rng = new DeterministicRandom(NodeSeed(node.Id), EventStream);
            EventOutcomeData picked = PickWeighted(evt.Outcomes, o => o.Weight, rng);

            var outcome = new ExpeditionNodeOutcome { NodeId = node.Id, Kind = node.Kind, RefId = node.RefId };

            switch (picked.Kind)
            {
                case EventOutcomeKind.GrantGold:
                {
                    var reward = new GrantedReward
                    {
                        Kind = RewardEntryKind.Gold,
                        Amount = rng.NextInt(picked.MinAmount, picked.MaxAmount + 1)
                    };
                    outcome.Rewards.Add(reward);
                    Bank(reward);
                    break;
                }

                case EventOutcomeKind.GrantMaterial:
                {
                    var reward = new GrantedReward
                    {
                        Kind = RewardEntryKind.Material,
                        RefId = picked.MaterialId,
                        Amount = rng.NextInt(picked.MinAmount, picked.MaxAmount + 1)
                    };
                    outcome.Rewards.Add(reward);
                    Bank(reward);
                    break;
                }

                case EventOutcomeKind.GrantBuffSkill:
                    State.RunBuffSkillIds.Add(picked.SkillId);
                    outcome.GrantedBuffSkillId = picked.SkillId;
                    break;

                case EventOutcomeKind.HealTeamPercent:
                    outcome.HealedPerMille = picked.MinAmount;
                    HealTeam(picked.MinAmount);
                    break;
            }

            State.TotalRngCalls += rng.CallCount;
            outcome.RunStatusAfter = State.Status;
            return outcome;
        }

        private ExpeditionNodeOutcome ResolveTreasure(ContentPack pack, ExpeditionNodeState node)
        {
            var outcome = new ExpeditionNodeOutcome { NodeId = node.Id, Kind = node.Kind, RefId = node.RefId };

            GrantedReward reward = RollReward(pack, node.RefId, node.Id);
            if (reward != null)
            {
                outcome.Rewards.Add(reward);
                Bank(reward);
            }

            outcome.RunStatusAfter = State.Status;
            return outcome;
        }

        private ExpeditionNodeOutcome ResolveRest(StageData stage, ExpeditionNodeState node)
        {
            HealTeam(stage.RestHealPerMille);

            return new ExpeditionNodeOutcome
            {
                NodeId = node.Id,
                Kind = node.Kind,
                RefId = node.RefId,
                HealedPerMille = stage.RestHealPerMille,
                RunStatusAfter = State.Status
            };
        }

        // ------------------------------------------------------------------
        // Team, HP and rewards
        // ------------------------------------------------------------------

        private static void BuildTeamSnapshot(
            ContentPack pack,
            ExpeditionRunState state,
            int leaderSlot,
            IEnumerable<KeyValuePair<int, OwnedCharacter>> placements)
        {
            var sorted = new List<KeyValuePair<int, OwnedCharacter>>(placements);
            sorted.Sort((a, b) => a.Key.CompareTo(b.Key));

            var seenSlots = new HashSet<int>();
            bool leaderPresent = false;

            foreach (KeyValuePair<int, OwnedCharacter> placement in sorted)
            {
                int slot = placement.Key;
                if (slot < 0 || slot >= Formation.SlotCount)
                {
                    throw new ExpeditionException(
                        "Slot " + slot + " is outside 0.." + (Formation.SlotCount - 1) + ".");
                }

                if (!seenSlots.Add(slot))
                {
                    throw new ExpeditionException("Slot " + slot + " is used twice.");
                }

                if (slot == leaderSlot)
                {
                    leaderPresent = true;
                }

                OwnedCharacter owned = placement.Value;
                StatBlock stats = Progression.ComputeStats(pack, owned);

                state.Team.Add(new RunTeamMemberState
                {
                    Slot = slot,
                    InstanceId = owned.InstanceId,
                    CharacterId = owned.CharacterId,
                    Level = owned.Level,
                    Focus = owned.Focus,
                    InheritedBonusPerMille = owned.InheritedBonusPerMille,
                    MaxHp = stats.MaxHp,
                    CurrentHp = stats.MaxHp
                });
            }

            if (!leaderPresent)
            {
                throw new ExpeditionException("Leader slot " + leaderSlot + " is empty.");
            }
        }

        /// <summary>
        /// Builds the attacker team for this run's next battle: locked-in stats, current
        /// (possibly partial) HP, and every run-scoped buff skill granted so far — appended to
        /// every unit rather than just one, so the buff still applies at battle start even if
        /// whichever unit picked it up is currently down. See docs/09-expedition-spec.md.
        /// </summary>
        private TeamDefinition BuildBattleTeam(ContentPack pack)
        {
            var runBuffs = new List<SkillDefinition>();
            foreach (string skillId in State.RunBuffSkillIds)
            {
                runBuffs.Add(pack.GetSkill(skillId));
            }

            var team = new TeamDefinition { TeamId = State.StageId, LeaderSlot = State.LeaderSlot };

            foreach (RunTeamMemberState member in State.Team)
            {
                CharacterData character = pack.GetCharacter(member.CharacterId);
                StatBlock stats = Progression.ComputeStats(
                    pack, character, member.Level, member.Focus, member.InheritedBonusPerMille);

                var skills = new List<SkillDefinition>(pack.ResolveSkills(character.SkillIds));
                skills.AddRange(runBuffs);

                team.Units.Add(new UnitDefinition
                {
                    InstanceId = member.InstanceId,
                    CharacterId = character.Id,
                    DisplayName = character.Name,
                    Affinity = character.Affinity,
                    Slot = member.Slot,
                    Stats = stats,
                    StartingHp = member.CurrentHp,
                    Skills = skills
                });
            }

            return team;
        }

        private void ApplyCarriedHp(BattleResult result)
        {
            foreach (UnitSnapshot snapshot in result.FinalUnits)
            {
                if (snapshot.Ref.Side != TeamSide.Attacker)
                {
                    continue;
                }

                foreach (RunTeamMemberState member in State.Team)
                {
                    if (member.Slot == snapshot.Ref.Slot)
                    {
                        member.CurrentHp = snapshot.Hp;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Restores a per-mille share of missing HP to every currently living member. A member
        /// already at 0 stays down: nothing outside a battle revives a unit, only a Revive skill
        /// firing on <c>OnAllyDown</c> in a later fight does, which is what gives losing a unit
        /// mid-run a cost that lasts more than one node.
        /// </summary>
        private void HealTeam(int perMille)
        {
            foreach (RunTeamMemberState member in State.Team)
            {
                if (!member.IsAlive)
                {
                    continue;
                }

                int missing = member.MaxHp - member.CurrentHp;
                if (missing <= 0)
                {
                    continue;
                }

                int healed = (int)((long)missing * perMille / 1000L);
                member.CurrentHp += healed;
                if (member.CurrentHp > member.MaxHp)
                {
                    member.CurrentHp = member.MaxHp;
                }
            }
        }

        private GrantedReward RollReward(ContentPack pack, string tableId, int nodeId)
        {
            RewardTableData table = pack.GetRewardTable(tableId);
            var rng = new DeterministicRandom(NodeSeed(nodeId), RewardStream);
            RewardEntryData picked = PickWeighted(table.Entries, e => e.Weight, rng);

            GrantedReward reward = null;
            if (picked.Kind != RewardEntryKind.Nothing)
            {
                int amount = picked.Kind == RewardEntryKind.Character
                    ? 1
                    : rng.NextInt(picked.MinAmount, picked.MaxAmount + 1);
                reward = new GrantedReward { Kind = picked.Kind, RefId = picked.RefId, Amount = amount };
            }

            State.TotalRngCalls += rng.CallCount;
            return reward;
        }

        private void Bank(GrantedReward reward)
        {
            switch (reward.Kind)
            {
                case RewardEntryKind.Gold:
                    State.BankedGold += reward.Amount;
                    break;

                case RewardEntryKind.Material:
                    BankMaterial(reward.RefId, reward.Amount);
                    break;

                case RewardEntryKind.Character:
                    State.BankedCharacterIds.Add(reward.RefId);
                    break;
            }
        }

        private void BankMaterial(string materialId, int amount)
        {
            foreach (MaterialStack stack in State.BankedMaterials)
            {
                if (stack.MaterialId == materialId)
                {
                    stack.Amount += amount;
                    return;
                }
            }

            State.BankedMaterials.Add(new MaterialStack { MaterialId = materialId, Amount = amount });
        }

        // ------------------------------------------------------------------
        // Seeding and helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Folds the stage id into the run seed so that reusing the same numeric seed across two
        /// different stages — an easy mistake for a caller to make — never makes their node
        /// seeds collide.
        /// </summary>
        private static ulong RunKey(ExpeditionRunState state)
        {
            return SeedMixer.Mix(state.Seed, SeedMixer.HashString(state.StageId));
        }

        /// <summary>
        /// The seed one specific node's battle, event or reward roll uses. Depends only on the
        /// run's seed, its stage id and the node's own id — never on anything resolved earlier in
        /// the run — so any single node is exactly as independently reproducible as a standalone
        /// battle: recomputing this value needs nothing but those three numbers.
        /// </summary>
        private ulong NodeSeed(int nodeId)
        {
            return SeedMixer.Mix(RunKey(State), (ulong)(nodeId + 1));
        }

        private ExpeditionNodeState NodeById(int id)
        {
            return State.Nodes[id];
        }

        private void RequireLiveContent(ContentPack pack)
        {
            if (pack.Version != State.ContentVersion)
            {
                throw new ExpeditionException(
                    "This run began under content " + State.ContentVersion + " but content "
                    + pack.Version + " was supplied. An in-flight expedition must finish under "
                    + "the version it began with (docs/05-content-pipeline.md).");
            }

            if (CombatRules.Version != State.RulesVersion)
            {
                throw new ExpeditionException(
                    "This run began under rules " + State.RulesVersion + " but this build "
                    + "implements " + CombatRules.Version + ".");
            }
        }

        private static ExpeditionLogEntry ToLogEntry(ExpeditionNodeOutcome outcome)
        {
            return new ExpeditionLogEntry
            {
                NodeId = outcome.NodeId,
                Kind = outcome.Kind,
                RefId = outcome.RefId,
                BattleOutcome = outcome.BattleResult?.Outcome,
                BattleChecksum = outcome.BattleResult?.Checksum,
                BattleRngCalls = outcome.BattleResult?.RngCalls,
                Rewards = outcome.Rewards,
                GrantedBuffSkillId = outcome.GrantedBuffSkillId,
                HealedPerMille = outcome.HealedPerMille
            };
        }

        /// <summary>Picks one item by relative weight. Every caller here rolls exactly once per
        /// invocation, so "how many items" never changes how many RNG words are drawn for a kind
        /// that was not picked.</summary>
        private static T PickWeighted<T>(List<T> items, Func<T, int> weightOf, DeterministicRandom rng)
        {
            int total = 0;
            foreach (T item in items)
            {
                total += weightOf(item);
            }

            int roll = rng.NextInt(0, total);
            int cumulative = 0;
            foreach (T item in items)
            {
                cumulative += weightOf(item);
                if (roll < cumulative)
                {
                    return item;
                }
            }

            // Unreachable once ContentPack.Validate has required a positive total weight.
            return items[items.Count - 1];
        }
    }
}
