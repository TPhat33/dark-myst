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

        private ContentPack(
            ContentManifest manifest,
            Dictionary<string, SkillDefinition> skills,
            Dictionary<string, CharacterData> characters,
            Dictionary<string, EnemyData> enemies,
            Dictionary<string, EncounterData> encounters,
            ProgressionData progression)
        {
            Manifest = manifest;
            _skills = skills;
            _characters = characters;
            _enemies = enemies;
            _encounters = encounters;
            Progression = progression;
        }

        public ContentManifest Manifest { get; }

        public ProgressionData Progression { get; }

        public string Version => Manifest.ContentVersion;

        public IReadOnlyCollection<CharacterData> Characters => _characters.Values;

        public IReadOnlyCollection<EnemyData> Enemies => _enemies.Values;

        public IReadOnlyCollection<EncounterData> Encounters => _encounters.Values;

        public IReadOnlyCollection<SkillDefinition> Skills => _skills.Values;

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

            ProgressionData progression = ReadJson<ProgressionData>(manifest.Progression);

            var pack = new ContentPack(manifest, skills, characters, enemies, encounters, progression);
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
    }
}
