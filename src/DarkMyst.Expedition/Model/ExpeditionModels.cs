using System.Collections.Generic;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using Newtonsoft.Json;

namespace DarkMyst.Expedition.Model
{
    /// <summary>How an expedition run currently stands.</summary>
    public enum RunStatus
    {
        InProgress = 0,

        /// <summary>The boss fell. Banked rewards are final; the run accepts no further choices.</summary>
        Cleared = 1,

        /// <summary>A battle node was lost. See <c>docs/09-expedition-spec.md</c> for what
        /// happens to rewards banked before the loss.</summary>
        Failed = 2
    }

    /// <summary>One node of the generated map. Fixed once <see cref="ExpeditionRun.Start"/>
    /// returns — nothing here changes as the run is played.</summary>
    public sealed class ExpeditionNodeState
    {
        /// <summary>Position in <see cref="ExpeditionRunState.Nodes"/>. Stable for the run's lifetime.</summary>
        public int Id { get; set; }

        /// <summary>0-based layer index. The boss node's layer is one past the last authored layer.</summary>
        public int Layer { get; set; }

        public NodeKind Kind { get; set; }

        /// <summary>Encounter id (Battle/Boss), event id (Event), reward table id (Treasure), or
        /// null (Rest) — whichever <see cref="Kind"/> calls for.</summary>
        public string RefId { get; set; }

        /// <summary>Nodes one layer forward that this node connects to. Never empty except for
        /// the boss node, which ends the run.</summary>
        public List<int> NextNodeIds { get; set; } = new List<int>();
    }

    /// <summary>One reachable option, as returned by <see cref="ExpeditionRun.AvailableChoices"/>.</summary>
    public sealed class ExpeditionChoice
    {
        public int NodeId { get; set; }

        public NodeKind Kind { get; set; }
    }

    /// <summary>One thing granted by a node: gold, a material stack, or a character.</summary>
    public sealed class GrantedReward
    {
        public RewardEntryKind Kind { get; set; }

        /// <summary>Material id or character id. Null for Gold and Nothing.</summary>
        public string RefId { get; set; }

        public int Amount { get; set; }
    }

    /// <summary>
    /// The persisted record of one resolved node. Deliberately lighter than the live
    /// <see cref="ExpeditionNodeOutcome"/>: a battle's full event log is reproducible on demand
    /// from <see cref="ExpeditionRunState.Seed"/> and this entry's <see cref="NodeId"/>, so only
    /// the checksum and call count — the same two fields <c>BattleResult</c> uses to prove two
    /// runs agree — need to survive a save. Keeping the full log here as well would make an
    /// expedition's save grow without bound over a long stage for no benefit a checksum does not
    /// already give.
    /// </summary>
    public sealed class ExpeditionLogEntry
    {
        public int NodeId { get; set; }

        public NodeKind Kind { get; set; }

        public string RefId { get; set; }

        public BattleOutcome? BattleOutcome { get; set; }

        public string BattleChecksum { get; set; }

        public long? BattleRngCalls { get; set; }

        public List<GrantedReward> Rewards { get; set; } = new List<GrantedReward>();

        public string GrantedBuffSkillId { get; set; }

        public int? HealedPerMille { get; set; }

        /// <summary>Same shape as <c>BattleEvent.ToCanonicalString()</c>: every field that can
        /// differ between two runs of the same seed, nothing that cannot.</summary>
        public string ToCanonicalString()
        {
            var rewardParts = new List<string>();
            foreach (GrantedReward reward in Rewards)
            {
                rewardParts.Add(reward.Kind + ":" + (reward.RefId ?? "-") + ":" + reward.Amount);
            }

            return string.Join("|", new[]
            {
                NodeId.ToString(),
                Kind.ToString(),
                RefId ?? "-",
                BattleOutcome.HasValue ? BattleOutcome.Value.ToString() : "-",
                BattleChecksum ?? "-",
                BattleRngCalls.HasValue ? BattleRngCalls.Value.ToString() : "-",
                string.Join(",", rewardParts),
                GrantedBuffSkillId ?? "-",
                HealedPerMille.HasValue ? HealedPerMille.Value.ToString() : "-"
            });
        }
    }

    /// <summary>
    /// What resolving one node actually produced. Returned by <see cref="ExpeditionRun.Choose"/>
    /// for immediate use — printing a log line, animating a fight, showing a reward popup — and
    /// carries the full <see cref="Combat.Model.BattleResult"/> for a battle or boss node so that
    /// specific fight is exactly as independently verifiable as a standalone one.
    /// </summary>
    public sealed class ExpeditionNodeOutcome
    {
        public int NodeId { get; set; }

        public NodeKind Kind { get; set; }

        public string RefId { get; set; }

        /// <summary>Set only for Battle and Boss nodes.</summary>
        public BattleResult BattleResult { get; set; }

        public List<GrantedReward> Rewards { get; set; } = new List<GrantedReward>();

        public string GrantedBuffSkillId { get; set; }

        public int? HealedPerMille { get; set; }

        public bool RunEnded { get; set; }

        public RunStatus RunStatusAfter { get; set; }
    }

    /// <summary>
    /// One team slot as locked in for the run. Snapshotted from the player's account at
    /// <see cref="ExpeditionRun.Start"/> rather than read live on every node: a level-up or
    /// evolve elsewhere while an expedition is in flight must not change a fight already
    /// mid-run, the same reasoning <c>docs/05-content-pipeline.md</c> applies to content
    /// versions.
    /// </summary>
    public sealed class RunTeamMemberState
    {
        public int Slot { get; set; }

        public string InstanceId { get; set; }

        public string CharacterId { get; set; }

        public int Level { get; set; }

        public EvolveFocus Focus { get; set; }

        public int InheritedBonusPerMille { get; set; }

        /// <summary>Cached from <c>Progression.ComputeStats</c> at Start; stats cannot change
        /// mid-run since level, focus and content version are all locked with it.</summary>
        public int MaxHp { get; set; }

        /// <summary>HP this member carries into its next battle. See
        /// <c>docs/09-expedition-spec.md</c> for why HP is not refilled between nodes.</summary>
        public int CurrentHp { get; set; }

        [JsonIgnore]
        public bool IsAlive => CurrentHp > 0;
    }

    public sealed class MaterialStack
    {
        public string MaterialId { get; set; }

        public int Amount { get; set; }
    }

    /// <summary>
    /// The whole of one expedition run's state. Plain data start to finish — no live RNG, no
    /// <c>ContentPack</c>, no <c>BattleSimulator</c> — so it serializes with
    /// <c>ContentPack.SerializerSettings</c> exactly like a content pack does, and a server can
    /// store, reload and resume it without this library's help beyond
    /// <see cref="ExpeditionRun.Resume"/>.
    /// </summary>
    public sealed class ExpeditionRunState
    {
        public string StageId { get; set; }

        public ulong Seed { get; set; }

        /// <summary>Pinned at Start. <see cref="ExpeditionRun.Resume"/> refuses to continue a run
        /// under a different value of either version — see <c>docs/05-content-pipeline.md</c>.</summary>
        public string ContentVersion { get; set; }

        public string RulesVersion { get; set; }

        public int LeaderSlot { get; set; }

        /// <summary>Sorted by <see cref="RunTeamMemberState.Slot"/> ascending and iterated only
        /// in that order, never as a lookup keyed some other way.</summary>
        public List<RunTeamMemberState> Team { get; set; } = new List<RunTeamMemberState>();

        /// <summary>The whole generated map, fixed at Start.</summary>
        public List<ExpeditionNodeState> Nodes { get; set; } = new List<ExpeditionNodeState>();

        public List<int> EntryNodeIds { get; set; } = new List<int>();

        /// <summary>Null before the first <see cref="ExpeditionRun.Choose"/> call.</summary>
        public int? CurrentNodeId { get; set; }

        public RunStatus Status { get; set; } = RunStatus.InProgress;

        /// <summary>
        /// Skill ids granted by events or treasure so far this run, injected into every unit's
        /// skill list for every subsequent battle. Never written back onto an
        /// <c>OwnedCharacter</c> — the moment the run ends this list is simply discarded, which
        /// is the entire mechanism that keeps a run-scoped buff from outliving its run.
        /// </summary>
        public List<string> RunBuffSkillIds { get; set; } = new List<string>();

        public int BankedGold { get; set; }

        public List<MaterialStack> BankedMaterials { get; set; } = new List<MaterialStack>();

        public List<string> BankedCharacterIds { get; set; } = new List<string>();

        public List<ExpeditionLogEntry> Log { get; set; } = new List<ExpeditionLogEntry>();

        /// <summary>Map generation draws plus every node's battle/reward/event draws. The
        /// run-level analogue of <c>BattleResult.RngCalls</c>.</summary>
        public long TotalRngCalls { get; set; }

        /// <summary>FNV-1a 64 over the canonical form of <see cref="Log"/>, the same recipe
        /// <c>BattleResult.ComputeChecksum</c> uses. Two runs that agree on every node they
        /// resolved agree on this one string.</summary>
        public string ComputeChecksum()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            foreach (ExpeditionLogEntry entry in Log)
            {
                foreach (byte b in System.Text.Encoding.UTF8.GetBytes(entry.ToCanonicalString()))
                {
                    unchecked
                    {
                        hash ^= b;
                        hash *= prime;
                    }
                }

                unchecked
                {
                    hash ^= (byte)'\n';
                    hash *= prime;
                }
            }

            return hash.ToString("x16");
        }
    }
}
