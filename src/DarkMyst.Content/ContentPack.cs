using System;
using System.Collections.Generic;
using System.IO;
using DarkMyst.Combat.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace DarkMyst.Content
{
    /// <summary>Raised when a content pack is missing something the game needs.</summary>
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// One immutable, versioned set of game data. Everything a designer can change without a
    /// code change lives here: characters, skills, enemies, encounters and progression rules.
    /// <para>
    /// The server keeps several versions loaded at once, because an owned character and an
    /// in-flight expedition each pin the version they were created under.
    /// </para>
    /// </summary>
    public sealed class ContentPack
    {
        private readonly Dictionary<string, SkillDefinition> _skills;
        private readonly Dictionary<string, CharacterData> _characters;
        private readonly Dictionary<string, EnemyData> _enemies;
        private readonly Dictionary<string, EncounterData> _encounters;
        private readonly Dictionary<string, StageData> _stages;
        private readonly Dictionary<string, RewardTableData> _rewardTables;
        private readonly Dictionary<string, EventData> _events;

        private ContentPack(
            ContentManifest manifest,
            Dictionary<string, SkillDefinition> skills,
            Dictionary<string, CharacterData> characters,
            Dictionary<string, EnemyData> enemies,
            Dictionary<string, EncounterData> encounters,
            Dictionary<string, StageData> stages,
            Dictionary<string, RewardTableData> rewardTables,
            Dictionary<string, EventData> events,
            ProgressionData progression)
        {
            Manifest = manifest;
            _skills = skills;
            _characters = characters;
            _enemies = enemies;
            _encounters = encounters;
            _stages = stages;
            _rewardTables = rewardTables;
            _events = events;
            Progression = progression;
        }

        public ContentManifest Manifest { get; }

        public ProgressionData Progression { get; }

        public string Version => Manifest.ContentVersion;

        public IReadOnlyCollection<CharacterData> Characters => _characters.Values;

        public IReadOnlyCollection<EnemyData> Enemies => _enemies.Values;

        public IReadOnlyCollection<EncounterData> Encounters => _encounters.Values;

        public IReadOnlyCollection<SkillDefinition> Skills => _skills.Values;

        public IReadOnlyCollection<StageData> Stages => _stages.Values;

        public IReadOnlyCollection<RewardTableData> RewardTables => _rewardTables.Values;

        public IReadOnlyCollection<EventData> Events => _events.Values;

        /// <summary>Serializer settings shared by the loader, the admin tool and the sim runner.</summary>
        public static JsonSerializerSettings SerializerSettings
        {
            get
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy()
                    },
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented,
                    // Silent typos in content are how a boss quietly loses a skill.
                    MissingMemberHandling = MissingMemberHandling.Error
                };
                settings.Converters.Add(new StringEnumConverter());
                return settings;
            }
        }

        public static ContentPack LoadFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new ContentException("Content directory not found: " + directory);
            }

            return Load(fileName =>
            {
                string path = Path.Combine(directory, fileName);
                if (!File.Exists(path))
                {
                    throw new ContentException("Content file not found: " + path);
                }

                return File.ReadAllText(path);
            });
        }

        /// <summary>
        /// Loads a pack from an arbitrary source, given a function that returns the text of a
        /// file by its name. Unity needs this on Android, where StreamingAssets lives inside
        /// the APK and can only be read through UnityWebRequest rather than System.IO.
        /// </summary>
        public static ContentPack Load(Func<string, string> readFile)
        {
            if (readFile == null)
            {
                throw new ArgumentNullException(nameof(readFile));
            }

            ContentManifest manifest = ParseJson<ContentManifest>("manifest.json", readFile("manifest.json"));

            if (string.IsNullOrEmpty(manifest.ContentVersion))
            {
                throw new ContentException("manifest.json has no contentVersion.");
            }

            T ReadJson<T>(string fileName) => ParseJson<T>(fileName, readFile(fileName));

            var skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
            foreach (SkillDefinition skill in ReadJson<SkillFile>(manifest.Skills).Skills)
            {
                if (string.IsNullOrEmpty(skill.Id))
                {
                    throw new ContentException("A skill in " + manifest.Skills + " has no id.");
                }

                if (skills.ContainsKey(skill.Id))
                {
                    throw new ContentException("Duplicate skill id '" + skill.Id + "'.");
                }

                skills.Add(skill.Id, skill);
            }

            var characters = new Dictionary<string, CharacterData>(StringComparer.Ordinal);
            foreach (CharacterData character in ReadJson<CharacterFile>(manifest.Characters).Characters)
            {
                if (characters.ContainsKey(character.Id))
                {
                    throw new ContentException("Duplicate character id '" + character.Id + "'.");
                }

                characters.Add(character.Id, character);
            }

            var enemies = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
            foreach (EnemyData enemy in ReadJson<EnemyFile>(manifest.Enemies).Enemies)
            {
                if (enemies.ContainsKey(enemy.Id))
                {
                    throw new ContentException("Duplicate enemy id '" + enemy.Id + "'.");
                }

                enemies.Add(enemy.Id, enemy);
            }

            var encounters = new Dictionary<string, EncounterData>(StringComparer.Ordinal);
            foreach (EncounterData encounter in ReadJson<EncounterFile>(manifest.Encounters).Encounters)
            {
                if (encounters.ContainsKey(encounter.Id))
                {
                    throw new ContentException("Duplicate encounter id '" + encounter.Id + "'.");
                }

                encounters.Add(encounter.Id, encounter);
            }

            ExpeditionFile expeditionFile = ReadJson<ExpeditionFile>(manifest.Stages);

            var rewardTables = new Dictionary<string, RewardTableData>(StringComparer.Ordinal);
            foreach (RewardTableData table in expeditionFile.RewardTables)
            {
                if (rewardTables.ContainsKey(table.Id))
                {
                    throw new ContentException("Duplicate reward table id '" + table.Id + "'.");
                }

                rewardTables.Add(table.Id, table);
            }

            var events = new Dictionary<string, EventData>(StringComparer.Ordinal);
            foreach (EventData evt in expeditionFile.Events)
            {
                if (events.ContainsKey(evt.Id))
                {
                    throw new ContentException("Duplicate event id '" + evt.Id + "'.");
                }

                events.Add(evt.Id, evt);
            }

            var stages = new Dictionary<string, StageData>(StringComparer.Ordinal);
            foreach (StageData stage in expeditionFile.Stages)
            {
                if (stages.ContainsKey(stage.Id))
                {
                    throw new ContentException("Duplicate stage id '" + stage.Id + "'.");
                }

                stages.Add(stage.Id, stage);
            }

            ProgressionData progression = ReadJson<ProgressionData>(manifest.Progression);

            var pack = new ContentPack(
                manifest, skills, characters, enemies, encounters, stages, rewardTables, events, progression);
            pack.Validate();
            return pack;
        }

        public SkillDefinition GetSkill(string id)
        {
            SkillDefinition skill;
            if (!_skills.TryGetValue(id, out skill))
            {
                throw new ContentException("Unknown skill id '" + id + "'.");
            }

            return skill;
        }

        public CharacterData GetCharacter(string id)
        {
            CharacterData character;
            if (!_characters.TryGetValue(id, out character))
            {
                throw new ContentException("Unknown character id '" + id + "'.");
            }

            return character;
        }

        public EnemyData GetEnemy(string id)
        {
            EnemyData enemy;
            if (!_enemies.TryGetValue(id, out enemy))
            {
                throw new ContentException("Unknown enemy id '" + id + "'.");
            }

            return enemy;
        }

        public EncounterData GetEncounter(string id)
        {
            EncounterData encounter;
            if (!_encounters.TryGetValue(id, out encounter))
            {
                throw new ContentException("Unknown encounter id '" + id + "'.");
            }

            return encounter;
        }

        public StageData GetStage(string id)
        {
            StageData stage;
            if (!_stages.TryGetValue(id, out stage))
            {
                throw new ContentException("Unknown stage id '" + id + "'.");
            }

            return stage;
        }

        public RewardTableData GetRewardTable(string id)
        {
            RewardTableData table;
            if (!_rewardTables.TryGetValue(id, out table))
            {
                throw new ContentException("Unknown reward table id '" + id + "'.");
            }

            return table;
        }

        public EventData GetEvent(string id)
        {
            EventData evt;
            if (!_events.TryGetValue(id, out evt))
            {
                throw new ContentException("Unknown event id '" + id + "'.");
            }

            return evt;
        }

        public List<SkillDefinition> ResolveSkills(IEnumerable<string> skillIds)
        {
            var resolved = new List<SkillDefinition>();
            foreach (string id in skillIds)
            {
                resolved.Add(GetSkill(id));
            }

            return resolved;
        }

        /// <summary>
        /// Cross-checks every reference in the pack. Runs at load time on the server and in CI,
        /// so a broken reference is caught before it reaches a player.
        /// </summary>
        public void Validate()
        {
            var problems = new List<string>();

            foreach (CharacterData character in _characters.Values)
            {
                if (string.IsNullOrEmpty(character.LineId))
                {
                    problems.Add("Character '" + character.Id + "' has no lineId.");
                }

                if (character.BaseStats.MaxHp <= 0)
                {
                    problems.Add("Character '" + character.Id + "' has no base HP.");
                }

                if (character.SkillIds.Count == 0)
                {
                    problems.Add("Character '" + character.Id + "' has no skills.");
                }

                foreach (string skillId in character.SkillIds)
                {
                    if (!_skills.ContainsKey(skillId))
                    {
                        problems.Add("Character '" + character.Id + "' references unknown skill '" + skillId + "'.");
                    }
                }

                if (!HasGuaranteedTurnAction(character.SkillIds))
                {
                    problems.Add(
                        "Character '" + character.Id + "' has no guaranteed OnAction skill; it could "
                        + "end up with nothing to do on its turn.");
                }

                if (!string.IsNullOrEmpty(character.NextStageId) && !_characters.ContainsKey(character.NextStageId))
                {
                    problems.Add(
                        "Character '" + character.Id + "' evolves into unknown character '"
                        + character.NextStageId + "'.");
                }

                if (!string.IsNullOrEmpty(character.NextStageId)
                    && Progression.Evolve.RequirementForStage(character.EvolveStage) == null)
                {
                    problems.Add(
                        "Character '" + character.Id + "' is stage " + character.EvolveStage
                        + " and can evolve, but progression.json has no requirement for that stage.");
                }
            }

            foreach (EnemyData enemy in _enemies.Values)
            {
                foreach (string skillId in enemy.SkillIds)
                {
                    if (!_skills.ContainsKey(skillId))
                    {
                        problems.Add("Enemy '" + enemy.Id + "' references unknown skill '" + skillId + "'.");
                    }
                }

                if (!HasGuaranteedTurnAction(enemy.SkillIds))
                {
                    problems.Add("Enemy '" + enemy.Id + "' has no guaranteed OnAction skill.");
                }
            }

            foreach (EncounterData encounter in _encounters.Values)
            {
                if (encounter.Units.Count == 0)
                {
                    problems.Add("Encounter '" + encounter.Id + "' has no units.");
                }

                var slots = new HashSet<int>();
                bool leaderPresent = false;
                foreach (EncounterUnitData unit in encounter.Units)
                {
                    if (!_enemies.ContainsKey(unit.EnemyId))
                    {
                        problems.Add(
                            "Encounter '" + encounter.Id + "' references unknown enemy '" + unit.EnemyId + "'.");
                    }

                    if (!slots.Add(unit.Slot))
                    {
                        problems.Add("Encounter '" + encounter.Id + "' uses slot " + unit.Slot + " twice.");
                    }

                    if (unit.Slot == encounter.LeaderSlot)
                    {
                        leaderPresent = true;
                    }
                }

                if (!leaderPresent)
                {
                    problems.Add("Encounter '" + encounter.Id + "' has an empty leader slot.");
                }
            }

            foreach (SkillDefinition skill in _skills.Values)
            {
                foreach (SkillEffect effect in skill.Effects)
                {
                    bool needsStatusId = effect.Kind == EffectKind.StatModifier
                        || effect.Kind == EffectKind.DamageOverTime
                        || effect.Kind == EffectKind.HealOverTime
                        || effect.Kind == EffectKind.Taunt;

                    if (needsStatusId && string.IsNullOrEmpty(effect.StatusId))
                    {
                        problems.Add(
                            "Skill '" + skill.Id + "' has a " + effect.Kind
                            + " effect without a statusId, so it can never be applied, stacked or cleansed.");
                    }
                }
            }

            ValidateRewardTables(problems);
            ValidateEvents(problems);
            ValidateStages(problems);

            if (Manifest.RulesVersion != Combat.CombatRules.Version)
            {
                problems.Add(
                    "Content was authored for rule set " + Manifest.RulesVersion
                    + " but this build implements " + Combat.CombatRules.Version + ".");
            }

            if (problems.Count > 0)
            {
                throw new ContentException(
                    "Content pack " + Manifest.ContentVersion + " is invalid:" + Environment.NewLine
                    + " - " + string.Join(Environment.NewLine + " - ", problems));
            }
        }

        /// <summary>Node kinds a layer can actually draw from its weighted mix, in a fixed order.
        /// <see cref="NodeKind.Boss"/> is deliberately excluded: it is placed by the generator,
        /// never rolled.</summary>
        private static readonly NodeKind[] DrawableNodeKinds =
        {
            NodeKind.Battle, NodeKind.Event, NodeKind.Treasure, NodeKind.Rest
        };

        private void ValidateRewardTables(List<string> problems)
        {
            foreach (RewardTableData table in _rewardTables.Values)
            {
                if (table.Entries.Count == 0)
                {
                    problems.Add("Reward table '" + table.Id + "' has no entries.");
                    continue;
                }

                long totalWeight = 0;
                foreach (RewardEntryData entry in table.Entries)
                {
                    if (entry.Weight <= 0)
                    {
                        problems.Add("Reward table '" + table.Id + "' has an entry with a non-positive weight.");
                    }

                    totalWeight += entry.Weight;

                    if (entry.Kind == RewardEntryKind.Material && string.IsNullOrEmpty(entry.RefId))
                    {
                        problems.Add(
                            "Reward table '" + table.Id + "' has a Material entry with no refId.");
                    }

                    if (entry.Kind == RewardEntryKind.Character)
                    {
                        if (string.IsNullOrEmpty(entry.RefId))
                        {
                            problems.Add(
                                "Reward table '" + table.Id + "' has a Character entry with no refId.");
                        }
                        else if (!_characters.ContainsKey(entry.RefId))
                        {
                            problems.Add(
                                "Reward table '" + table.Id + "' drops unknown character '" + entry.RefId + "'.");
                        }
                    }

                    if ((entry.Kind == RewardEntryKind.Gold || entry.Kind == RewardEntryKind.Material)
                        && entry.MinAmount > entry.MaxAmount)
                    {
                        problems.Add(
                            "Reward table '" + table.Id + "' has an entry whose minAmount exceeds maxAmount.");
                    }
                }

                if (totalWeight <= 0)
                {
                    problems.Add("Reward table '" + table.Id + "' has no entry with positive weight to roll.");
                }
            }
        }

        private void ValidateEvents(List<string> problems)
        {
            foreach (EventData evt in _events.Values)
            {
                if (evt.Outcomes.Count == 0)
                {
                    problems.Add("Event '" + evt.Id + "' has no outcomes.");
                    continue;
                }

                long totalWeight = 0;
                foreach (EventOutcomeData outcome in evt.Outcomes)
                {
                    if (outcome.Weight <= 0)
                    {
                        problems.Add("Event '" + evt.Id + "' has an outcome with a non-positive weight.");
                    }

                    totalWeight += outcome.Weight;

                    switch (outcome.Kind)
                    {
                        case EventOutcomeKind.GrantBuffSkill:
                            ValidateBuffSkillReference(evt.Id, outcome.SkillId, problems);
                            break;

                        case EventOutcomeKind.GrantMaterial:
                            if (string.IsNullOrEmpty(outcome.MaterialId))
                            {
                                problems.Add(
                                    "Event '" + evt.Id + "' has a GrantMaterial outcome with no materialId.");
                            }

                            break;

                        case EventOutcomeKind.GrantGold:
                            if (outcome.MinAmount > outcome.MaxAmount)
                            {
                                problems.Add(
                                    "Event '" + evt.Id + "' has a GrantGold outcome whose minAmount exceeds maxAmount.");
                            }

                            break;

                        case EventOutcomeKind.HealTeamPercent:
                            if (outcome.MinAmount <= 0)
                            {
                                problems.Add(
                                    "Event '" + evt.Id
                                    + "' has a HealTeamPercent outcome that heals nothing (minAmount must be > 0).");
                            }

                            break;
                    }
                }

                if (totalWeight <= 0)
                {
                    problems.Add("Event '" + evt.Id + "' has no outcome with positive weight to roll.");
                }
            }
        }

        /// <summary>
        /// A run-scoped buff is injected into every unit's own skill list (see
        /// <c>DarkMyst.Expedition.ExpeditionRun</c>), so the skill it names must actually fire
        /// there: an <see cref="TriggerKind.OnAction"/> skill would never trigger at battle
        /// start, and a leader-only skill would silently do nothing for whichever unit is not
        /// occupying the leader slot that battle.
        /// </summary>
        private void ValidateBuffSkillReference(string eventId, string skillId, List<string> problems)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                problems.Add("Event '" + eventId + "' has a GrantBuffSkill outcome with no skillId.");
                return;
            }

            SkillDefinition skill;
            if (!_skills.TryGetValue(skillId, out skill))
            {
                problems.Add("Event '" + eventId + "' grants unknown skill '" + skillId + "' as a buff.");
                return;
            }

            if (skill.Trigger != TriggerKind.OnBattleStart)
            {
                problems.Add(
                    "Event '" + eventId + "' grants skill '" + skillId + "' as a run buff, but its trigger is "
                    + skill.Trigger + " instead of OnBattleStart, so it would never apply.");
            }

            if (skill.IsLeaderSkill)
            {
                problems.Add(
                    "Event '" + eventId + "' grants skill '" + skillId
                    + "' as a run buff, but it is a leader skill: it would stop applying the moment the "
                    + "unit holding it is not in the leader slot, or is downed.");
            }
        }

        private void ValidateStages(List<string> problems)
        {
            foreach (StageData stage in _stages.Values)
            {
                if (string.IsNullOrEmpty(stage.Id))
                {
                    problems.Add("A stage has no id.");
                    continue;
                }

                if (stage.RecommendedLevel <= 0)
                {
                    problems.Add("Stage '" + stage.Id + "' has a non-positive recommendedLevel.");
                }

                if (stage.RestHealPerMille < 0)
                {
                    problems.Add("Stage '" + stage.Id + "' has a negative restHealPerMille.");
                }

                if (stage.BranchChancePerMille < 0 || stage.BranchChancePerMille > 1000)
                {
                    problems.Add("Stage '" + stage.Id + "' has a branchChancePerMille outside 0..1000.");
                }

                if (string.IsNullOrEmpty(stage.BossEncounterId))
                {
                    problems.Add("Stage '" + stage.Id + "' has no bossEncounterId.");
                }
                else if (!_encounters.ContainsKey(stage.BossEncounterId))
                {
                    problems.Add(
                        "Stage '" + stage.Id + "' references unknown boss encounter '"
                        + stage.BossEncounterId + "'.");
                }

                if (!string.IsNullOrEmpty(stage.ClearRewardTableId) && !_rewardTables.ContainsKey(stage.ClearRewardTableId))
                {
                    problems.Add(
                        "Stage '" + stage.Id + "' references unknown clear reward table '"
                        + stage.ClearRewardTableId + "'.");
                }

                if (!string.IsNullOrEmpty(stage.NodeRewardTableId) && !_rewardTables.ContainsKey(stage.NodeRewardTableId))
                {
                    problems.Add(
                        "Stage '" + stage.Id + "' references unknown node reward table '"
                        + stage.NodeRewardTableId + "'.");
                }

                if (stage.Layers.Count == 0)
                {
                    problems.Add("Stage '" + stage.Id + "' has no layers, so it cannot generate a map.");
                    continue;
                }

                for (int i = 0; i < stage.Layers.Count; i++)
                {
                    ValidateLayer(stage, i, problems);
                }
            }
        }

        private void ValidateLayer(StageData stage, int layerIndex, List<string> problems)
        {
            StageLayerData layer = stage.Layers[layerIndex];
            string where = "Stage '" + stage.Id + "' layer " + layerIndex;

            if (layer.NodeCount <= 0)
            {
                problems.Add(where + " has a non-positive nodeCount.");
                return;
            }

            int weightOf(NodeKind kind)
            {
                int value;
                return layer.NodeWeights != null && layer.NodeWeights.TryGetValue(kind, out value) ? value : 0;
            }

            int bossWeight;
            if (layer.NodeWeights != null
                && layer.NodeWeights.TryGetValue(NodeKind.Boss, out bossWeight) && bossWeight > 0)
            {
                problems.Add(
                    where + " lists a weight for Boss, but Boss is only ever the single node "
                    + "placed after the last layer, never drawn within one.");
            }

            long totalWeight = 0;
            foreach (NodeKind kind in DrawableNodeKinds)
            {
                totalWeight += weightOf(kind);
            }

            if (totalWeight <= 0)
            {
                problems.Add(
                    where + " has no node kind with positive weight, so it can never generate a node.");
                return;
            }

            if (weightOf(NodeKind.Battle) > 0)
            {
                ValidatePool(where, "encounterIds", layer.EncounterIds, _encounters.ContainsKey, problems);
            }

            if (weightOf(NodeKind.Event) > 0)
            {
                ValidatePool(where, "eventIds", layer.EventIds, _events.ContainsKey, problems);
            }

            if (weightOf(NodeKind.Treasure) > 0)
            {
                if (string.IsNullOrEmpty(layer.TreasureTableId))
                {
                    problems.Add(
                        where + " can draw a Treasure node but has no treasureTableId.");
                }
                else if (!_rewardTables.ContainsKey(layer.TreasureTableId))
                {
                    problems.Add(
                        where + " references unknown treasure table '" + layer.TreasureTableId + "'.");
                }
            }
        }

        private static void ValidatePool(
            string where, string fieldName, List<string> ids, Func<string, bool> exists, List<string> problems)
        {
            if (ids == null || ids.Count == 0)
            {
                problems.Add(where + " can draw a node kind whose pool (" + fieldName + ") is empty.");
                return;
            }

            foreach (string id in ids)
            {
                if (!exists(id))
                {
                    problems.Add(where + " references unknown id '" + id + "' in " + fieldName + ".");
                }
            }
        }

        private bool HasGuaranteedTurnAction(IEnumerable<string> skillIds)
        {
            foreach (string id in skillIds)
            {
                SkillDefinition skill;
                if (!_skills.TryGetValue(id, out skill))
                {
                    continue;
                }

                if (skill.Trigger == TriggerKind.OnAction
                    && !skill.IsLeaderSkill
                    && skill.ActivationChancePerMille >= 1000
                    && skill.CooldownRounds <= 0
                    && skill.InitialCooldownRounds <= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static T ParseJson<T>(string fileName, string text)
        {
            if (text == null)
            {
                throw new ContentException("Content file not found: " + fileName);
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(text, SerializerSettings);
            }
            catch (JsonException ex)
            {
                throw new ContentException("Could not read " + fileName + ": " + ex.Message);
            }
        }

        private sealed class SkillFile
        {
            public List<SkillDefinition> Skills { get; set; } = new List<SkillDefinition>();
        }

        private sealed class CharacterFile
        {
            public List<CharacterData> Characters { get; set; } = new List<CharacterData>();
        }

        private sealed class EnemyFile
        {
            public List<EnemyData> Enemies { get; set; } = new List<EnemyData>();
        }

        private sealed class EncounterFile
        {
            public List<EncounterData> Encounters { get; set; } = new List<EncounterData>();
        }

        private sealed class ExpeditionFile
        {
            public List<RewardTableData> RewardTables { get; set; } = new List<RewardTableData>();
            public List<EventData> Events { get; set; } = new List<EventData>();
            public List<StageData> Stages { get; set; } = new List<StageData>();
        }
    }
}
