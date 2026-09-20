using System.Collections.Generic;
using DarkMyst.Combat.Model;

namespace DarkMyst.Content
{
    /// <summary>What a stat block looks like in content JSON. Missing fields default to 0.</summary>
    public sealed class StatBlockData
    {
        public int MaxHp { get; set; }
        public int Attack { get; set; }
        public int Magic { get; set; }
        public int Defense { get; set; }
        public int Resist { get; set; }
        public int Speed { get; set; }
        public int CritRate { get; set; }
        public int CritDamage { get; set; }

        public StatBlock ToStatBlock()
        {
            return new StatBlock
            {
                MaxHp = MaxHp,
                Attack = Attack,
                Magic = Magic,
                Defense = Defense,
                Resist = Resist,
                Speed = Speed,
                CritRate = CritRate,
                CritDamage = CritDamage
            };
        }
    }

    /// <summary>
    /// One stage of one character line. "Ashen Knight I" and "Ashen Knight II" are two of
    /// these sharing a <see cref="LineId"/>.
    /// </summary>
    public sealed class CharacterData
    {
        public string Id { get; set; }

        /// <summary>Groups the evolve stages of one character. Same-line fodder is worth more.</summary>
        public string LineId { get; set; }

        public string Name { get; set; }

        public Affinity Affinity { get; set; } = Affinity.Neutral;

        /// <summary>Free-form designer label (Vanguard, Breaker, Mender, ...). Not read by the engine.</summary>
        public string Role { get; set; }

        /// <summary>1-5. Feeds the evolve essence formula and the drop tables.</summary>
        public int Rarity { get; set; } = 1;

        /// <summary>1-based evolve stage.</summary>
        public int EvolveStage { get; set; } = 1;

        /// <summary>Character id this becomes on evolve. Null for the final stage.</summary>
        public string NextStageId { get; set; }

        public StatBlockData BaseStats { get; set; } = new StatBlockData();

        /// <summary>Added per level above 1. Level 1 is exactly <see cref="BaseStats"/>.</summary>
        public StatBlockData GrowthPerLevel { get; set; } = new StatBlockData();

        public List<string> SkillIds { get; set; } = new List<string>();

        /// <summary>Short in-game blurb. Presentation only.</summary>
        public string Flavor { get; set; }
    }

    /// <summary>An authored enemy. Fixed stats, no progression, no ownership.</summary>
    public sealed class EnemyData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Affinity Affinity { get; set; } = Affinity.Neutral;
        public StatBlockData Stats { get; set; } = new StatBlockData();
        public List<string> SkillIds { get; set; } = new List<string>();
    }

    public sealed class EncounterUnitData
    {
        public string EnemyId { get; set; }
        public int Slot { get; set; }

        /// <summary>Per-mille multiplier applied to every stat, for scaling an encounter up.</summary>
        public int StatScalePerMille { get; set; } = 1000;
    }

    /// <summary>One fight as authored by a designer.</summary>
    public sealed class EncounterData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int LeaderSlot { get; set; }
        public List<EncounterUnitData> Units { get; set; } = new List<EncounterUnitData>();
    }

    // ------------------------------------------------------------------
    // Progression
    // ------------------------------------------------------------------

    /// <summary>The player's stat-focus choice made at evolve time.</summary>
    public enum EvolveFocus
    {
        None = 0,
        Offense = 1,
        Guard = 2,
        Swift = 3,
        Arcane = 4
    }

    public sealed class EvolveRequirementData
    {
        /// <summary>Stage the character must currently be at.</summary>
        public int FromStage { get; set; }

        public int RequiredLevel { get; set; }

        public int FodderCount { get; set; }

        /// <summary>How many of the fodder units must come from the same character line.</summary>
        public int SameLineFodderRequired { get; set; }

        public int Gold { get; set; }

        public Dictionary<string, int> Materials { get; set; } = new Dictionary<string, int>();
    }

    public sealed class EvolveRulesData
    {
        public int EssencePerLevel { get; set; } = 10;
        public int EssencePerRarityStep { get; set; } = 50;
        public int EssencePerStage { get; set; } = 100;

        /// <summary>Flat essence added for each fodder unit from the same character line.</summary>
        public int SameLineBonusEssence { get; set; } = 20;

        /// <summary>Share of essence kept from fodder of an unrelated affinity, in per-mille.</summary>
        public int OffAffinityEssencePerMille { get; set; }

        public int SameAffinityEssencePerMille { get; set; } = 500;

        public int SameLineEssencePerMille { get; set; } = 1000;

        /// <summary>Essence needed for one per-mille of inherited bonus.</summary>
        public int EssencePerBonusPerMille { get; set; } = 10;

        /// <summary>Hard cap on the lifetime inherited bonus of one character, in per-mille.</summary>
        public int InheritedBonusCapPerMille { get; set; } = 300;

        /// <summary>Which stats the inherited bonus touches. Speed and crit are deliberately out.</summary>
        public List<Stat> InheritedStats { get; set; } = new List<Stat>
        {
            Stat.MaxHp, Stat.Attack, Stat.Magic, Stat.Defense, Stat.Resist
        };

        /// <summary>Per-mille stat deltas granted by each focus. Negative values are the trade-off.</summary>
        public Dictionary<EvolveFocus, Dictionary<Stat, int>> Focuses { get; set; }
            = new Dictionary<EvolveFocus, Dictionary<Stat, int>>();

        public List<EvolveRequirementData> Requirements { get; set; } = new List<EvolveRequirementData>();

        public EvolveRequirementData RequirementForStage(int stage)
        {
            foreach (EvolveRequirementData requirement in Requirements)
            {
                if (requirement.FromStage == stage)
                {
                    return requirement;
                }
            }

            return null;
        }
    }

    public sealed class ProgressionData
    {
        /// <summary>Level cap per evolve stage, indexed from stage 1.</summary>
        public List<int> MaxLevelByStage { get; set; } = new List<int> { 20, 40, 60 };

        public EvolveRulesData Evolve { get; set; } = new EvolveRulesData();

        public int MaxLevelForStage(int stage)
        {
            if (stage < 1 || stage > MaxLevelByStage.Count)
            {
                return MaxLevelByStage.Count > 0 ? MaxLevelByStage[MaxLevelByStage.Count - 1] : 1;
            }

            return MaxLevelByStage[stage - 1];
        }
    }

    /// <summary>Lists the files that make up one content version.</summary>
    public sealed class ContentManifest
    {
        /// <summary>
        /// Bumped on every content change. Stored on each owned character and on each in-flight
        /// expedition, so an update never changes a recipe or a boss mid-run.
        /// </summary>
        public string ContentVersion { get; set; }

        /// <summary>Rule-set version this content was authored and balanced against.</summary>
        public string RulesVersion { get; set; }

        public string Skills { get; set; } = "skills.json";
        public string Characters { get; set; } = "characters.json";
        public string Enemies { get; set; } = "enemies.json";
        public string Progression { get; set; } = "progression.json";
        public string Encounters { get; set; } = "encounters.json";
    }
}
