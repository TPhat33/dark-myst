using System.Collections.Generic;

namespace DarkMyst.Combat.Model
{
    /// <summary>
    /// A combat-ready snapshot of one character. Progression, evolve bonuses and gear are all
    /// resolved before this point: the simulator never reads a player's account.
    /// </summary>
    public sealed class UnitDefinition
    {
        /// <summary>Owned-character id, or a stable id for an authored enemy. Appears in the log.</summary>
        public string InstanceId { get; set; }

        /// <summary>Character line id from content, for presentation and analytics.</summary>
        public string CharacterId { get; set; }

        public string DisplayName { get; set; }

        public Affinity Affinity { get; set; } = Affinity.Neutral;

        /// <summary>0-4. Slots 0-1 are the front row.</summary>
        public int Slot { get; set; }

        public StatBlock Stats { get; set; }

        public List<SkillDefinition> Skills { get; set; } = new List<SkillDefinition>();
    }

    public sealed class TeamDefinition
    {
        public string TeamId { get; set; }

        /// <summary>Slot whose leader skills are active. Must match one of the units' slots.</summary>
        public int LeaderSlot { get; set; }

        public List<UnitDefinition> Units { get; set; } = new List<UnitDefinition>();
    }

    /// <summary>
    /// Everything the simulator needs. Two identical requests must produce two identical
    /// results, on any platform, forever — that is the whole contract of this library.
    /// </summary>
    public sealed class BattleRequest
    {
        public ulong Seed { get; set; }

        /// <summary>Rule-set version the caller expects. Validated against <see cref="CombatRules.Version"/>.</summary>
        public string RulesVersion { get; set; } = CombatRules.Version;

        /// <summary>Content pack version the unit definitions came from. Echoed into the result.</summary>
        public string ContentVersion { get; set; }

        public TeamDefinition Attacker { get; set; }

        public TeamDefinition Defender { get; set; }

        /// <summary>Tuning values. Defaults to <see cref="CombatRules.Default"/> when null.</summary>
        public CombatRules Rules { get; set; }
    }
}
