using System.Collections.Generic;

namespace DarkMyst.Content
{
    /// <summary>
    /// What a node on an expedition map can be. <see cref="Boss"/> is never drawn from a layer's
    /// weighted mix — it is always the single node that ends the stage, placed by the generator
    /// rather than by content. See <c>docs/09-expedition-spec.md</c>.
    /// </summary>
    public enum NodeKind
    {
        Battle = 0,
        Event = 1,
        Treasure = 2,
        Rest = 3,
        Boss = 4
    }

    /// <summary>What a reward roll can produce.</summary>
    public enum RewardEntryKind
    {
        Gold = 0,
        Material = 1,
        Character = 2,

        /// <summary>An explicit whiff. Kept as its own entry so drop rates can be tuned without
        /// every table secretly guaranteeing something.</summary>
        Nothing = 3
    }

    /// <summary>What an authored event can do to the run when it resolves.</summary>
    public enum EventOutcomeKind
    {
        Nothing = 0,
        GrantGold = 1,
        GrantMaterial = 2,

        /// <summary>Injects <see cref="EventOutcomeData.SkillId"/> as a run-scoped buff. See
        /// <c>docs/09-expedition-spec.md</c> for why this reuses <c>TriggerKind.OnBattleStart</c>
        /// instead of a parallel buff system.</summary>
        GrantBuffSkill = 3,

        /// <summary>Restores a per-mille share of missing HP to every living team member.</summary>
        HealTeamPercent = 4
    }

    /// <summary>
    /// One weighted entry in a reward table. A table is rolled once per invocation: exactly one
    /// entry is picked, never several, so "average rewards per run" stays a simple thing to
    /// reason about and tune.
    /// </summary>
    public sealed class RewardEntryData
    {
        /// <summary>Relative weight, not a per-mille rate — the table's total need not be 1000.</summary>
        public int Weight { get; set; } = 1;

        public RewardEntryKind Kind { get; set; } = RewardEntryKind.Nothing;

        /// <summary>Material id or character id. Unused (and ignored) for Gold and Nothing.</summary>
        public string RefId { get; set; }

        public int MinAmount { get; set; } = 1;

        public int MaxAmount { get; set; } = 1;
    }

    public sealed class RewardTableData
    {
        public string Id { get; set; }

        public List<RewardEntryData> Entries { get; set; } = new List<RewardEntryData>();
    }

    /// <summary>One possible resolution of an authored event, picked by weight like a reward entry.</summary>
    public sealed class EventOutcomeData
    {
        public int Weight { get; set; } = 1;

        public EventOutcomeKind Kind { get; set; } = EventOutcomeKind.Nothing;

        /// <summary>Skill id for <see cref="EventOutcomeKind.GrantBuffSkill"/>.</summary>
        public string SkillId { get; set; }

        /// <summary>Material id for <see cref="EventOutcomeKind.GrantMaterial"/>.</summary>
        public string MaterialId { get; set; }

        /// <summary>
        /// Amount range for Gold/Material, or the per-mille share of missing HP restored for
        /// HealTeamPercent (only <see cref="MinAmount"/> is read in that case).
        /// </summary>
        public int MinAmount { get; set; }

        public int MaxAmount { get; set; }
    }

    public sealed class EventData
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public List<EventOutcomeData> Outcomes { get; set; } = new List<EventOutcomeData>();
    }

    /// <summary>
    /// One ring of the map. Nodes in this layer connect forward into the next layer (or into the
    /// stage's boss, for the last layer) — see <c>ExpeditionMapGenerator</c> in
    /// <c>DarkMyst.Expedition</c> for how the graph itself is built.
    /// </summary>
    public sealed class StageLayerData
    {
        /// <summary>How many nodes this layer generates.</summary>
        public int NodeCount { get; set; }

        /// <summary>
        /// Relative weight per <see cref="NodeKind"/>. <see cref="NodeKind.Boss"/> must not
        /// appear here — it is placed once, after every layer, never drawn.
        /// </summary>
        public Dictionary<NodeKind, int> NodeWeights { get; set; } = new Dictionary<NodeKind, int>();

        /// <summary>Encounter pool a <see cref="NodeKind.Battle"/> node in this layer draws from.</summary>
        public List<string> EncounterIds { get; set; } = new List<string>();

        /// <summary>Event pool a <see cref="NodeKind.Event"/> node in this layer draws from.</summary>
        public List<string> EventIds { get; set; } = new List<string>();

        /// <summary>Reward table a <see cref="NodeKind.Treasure"/> node in this layer rolls.</summary>
        public string TreasureTableId { get; set; }
    }

    /// <summary>
    /// One farmable expedition: a seeded node graph ending in a boss. See
    /// <c>docs/09-expedition-spec.md</c> for the rules a stage can and cannot express.
    /// </summary>
    public sealed class StageData
    {
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>Informational only — simrunner and the client use it, the rules do not gate on it.</summary>
        public int RecommendedLevel { get; set; }

        public List<StageLayerData> Layers { get; set; } = new List<StageLayerData>();

        public string BossEncounterId { get; set; }

        /// <summary>Rolled once, guaranteed, when the boss falls.</summary>
        public string ClearRewardTableId { get; set; }

        /// <summary>
        /// Rolled once per won <see cref="NodeKind.Battle"/> node (not the boss, which uses
        /// <see cref="ClearRewardTableId"/> instead). Null means plain battle nodes pay out
        /// nothing beyond clearing the node itself.
        /// </summary>
        public string NodeRewardTableId { get; set; }

        /// <summary>Per-mille share of missing HP a <see cref="NodeKind.Rest"/> node restores.</summary>
        public int RestHealPerMille { get; set; } = 500;

        /// <summary>
        /// Chance, per source node, of a second edge into the next layer. Zero collapses the map
        /// into a single forced line; higher values give the player more of a real path to pick.
        /// </summary>
        public int BranchChancePerMille { get; set; } = 350;

        /// <summary>
        /// Whether gold, materials and characters banked before a losing battle are kept. See
        /// <c>docs/09-expedition-spec.md</c> for why the default is true.
        /// </summary>
        public bool KeepRewardsOnDefeat { get; set; } = true;
    }
}
