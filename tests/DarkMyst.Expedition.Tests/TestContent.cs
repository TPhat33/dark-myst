using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Content;

namespace DarkMyst.Expedition.Tests
{
    /// <summary>
    /// A tiny, fully controlled content pack for expedition unit tests — the same idea as
    /// <c>DarkMyst.Combat.Tests.TestUnits</c>, but at the content-pack level, so a test can pin
    /// an exact map shape, a guaranteed win, a guaranteed loss, or a guaranteed event outcome
    /// without depending on the real, tunable game content in <c>content/</c>.
    /// <para>
    /// <c>enc_test_weak</c> is a single one-HP enemy that is faster than the hero, so every
    /// fight against it is exactly one round: the enemy lands one hit first (proving something
    /// for the HP-carry-over tests), then the hero's guaranteed attack kills it (proving the
    /// fight is always won). <c>enc_test_strong</c> is a single enemy that one-shots the hero
    /// before the hero can act, for the "a lost battle ends the run" tests.
    /// </para>
    /// </summary>
    internal static class TestContent
    {
        public static ContentPack Load(string contentVersion = "test.1")
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Manifest(contentVersion),
                ["skills.json"] = Skills,
                ["characters.json"] = Characters,
                ["enemies.json"] = Enemies,
                ["encounters.json"] = Encounters,
                ["progression.json"] = Progression,
                ["stages.json"] = Stages
            };

            return ContentPack.Load(name =>
            {
                string text;
                if (!files.TryGetValue(name, out text))
                {
                    throw new ContentException("Test content has no file '" + name + "'.");
                }

                return text;
            });
        }

        private static string Manifest(string contentVersion)
        {
            return @"{
                ""contentVersion"": """ + contentVersion + @""",
                ""rulesVersion"": """ + CombatRules.Version + @""",
                ""skills"": ""skills.json"",
                ""characters"": ""characters.json"",
                ""enemies"": ""enemies.json"",
                ""progression"": ""progression.json"",
                ""encounters"": ""encounters.json"",
                ""stages"": ""stages.json""
            }";
        }

        public const string HeroCharacterId = "chr_test_hero";
        public const string HeroInstanceId = "own_test_hero";
        public const string RelicBuffSkillId = "skl_test_relic_defense";
        public const string RelicBuffStatusId = "st_test_relic";

        private const string Skills = @"{
            ""skills"": [
                {
                    ""id"": ""skl_test_hero_atk"",
                    ""name"": ""Hero Strike"",
                    ""trigger"": ""OnAction"",
                    ""effects"": [
                        { ""kind"": ""Damage"", ""target"": ""RandomEnemy"", ""damageKind"": ""Physical"",
                          ""powerPerMille"": 1000, ""canCrit"": false }
                    ]
                },
                {
                    ""id"": ""skl_test_enemy_atk"",
                    ""name"": ""Enemy Strike"",
                    ""trigger"": ""OnAction"",
                    ""effects"": [
                        { ""kind"": ""Damage"", ""target"": ""RandomEnemy"", ""damageKind"": ""Physical"",
                          ""powerPerMille"": 1000, ""canCrit"": false }
                    ]
                },
                {
                    ""id"": ""skl_test_relic_defense"",
                    ""name"": ""Test Relic"",
                    ""trigger"": ""OnBattleStart"",
                    ""effects"": [
                        { ""kind"": ""StatModifier"", ""target"": ""AllAllies"", ""statusId"": ""st_test_relic"",
                          ""modifiedStat"": ""Defense"", ""amountPerMille"": 500, ""durationRounds"": 99,
                          ""stackRule"": ""Ignore"" }
                    ]
                }
            ]
        }";

        private const string Characters = @"{
            ""characters"": [
                {
                    ""id"": ""chr_test_hero"",
                    ""lineId"": ""line_test_hero"",
                    ""name"": ""Test Hero"",
                    ""affinity"": ""Neutral"",
                    ""rarity"": 1,
                    ""evolveStage"": 1,
                    ""baseStats"": { ""maxHp"": 1000, ""attack"": 80, ""magic"": 10, ""defense"": 20,
                                     ""resist"": 20, ""speed"": 100, ""critRate"": 0, ""critDamage"": 0 },
                    ""growthPerLevel"": { ""maxHp"": 0, ""attack"": 0, ""magic"": 0, ""defense"": 0,
                                          ""resist"": 0, ""speed"": 0, ""critRate"": 0, ""critDamage"": 0 },
                    ""skillIds"": [""skl_test_hero_atk""]
                }
            ]
        }";

        private const string Enemies = @"{
            ""enemies"": [
                {
                    ""id"": ""enm_test_weak"",
                    ""name"": ""Weak Foe"",
                    ""affinity"": ""Neutral"",
                    ""stats"": { ""maxHp"": 1, ""attack"": 50, ""magic"": 0, ""defense"": 0,
                                 ""resist"": 0, ""speed"": 200, ""critRate"": 0, ""critDamage"": 0 },
                    ""skillIds"": [""skl_test_enemy_atk""]
                },
                {
                    ""id"": ""enm_test_strong"",
                    ""name"": ""Overwhelming Foe"",
                    ""affinity"": ""Neutral"",
                    ""stats"": { ""maxHp"": 999999, ""attack"": 999999, ""magic"": 0, ""defense"": 0,
                                 ""resist"": 0, ""speed"": 200, ""critRate"": 0, ""critDamage"": 0 },
                    ""skillIds"": [""skl_test_enemy_atk""]
                }
            ]
        }";

        private const string Encounters = @"{
            ""encounters"": [
                {
                    ""id"": ""enc_test_weak"",
                    ""name"": ""Weak"",
                    ""leaderSlot"": 0,
                    ""units"": [ { ""enemyId"": ""enm_test_weak"", ""slot"": 0 } ]
                },
                {
                    ""id"": ""enc_test_strong"",
                    ""name"": ""Strong"",
                    ""leaderSlot"": 0,
                    ""units"": [ { ""enemyId"": ""enm_test_strong"", ""slot"": 0 } ]
                }
            ]
        }";

        private const string Progression = @"{
            ""maxLevelByStage"": [20],
            ""evolve"": {
                ""essencePerLevel"": 10, ""essencePerRarityStep"": 50, ""essencePerStage"": 100,
                ""sameLineBonusEssence"": 20, ""sameLineEssencePerMille"": 1000,
                ""sameAffinityEssencePerMille"": 500, ""offAffinityEssencePerMille"": 0,
                ""essencePerBonusPerMille"": 10, ""inheritedBonusCapPerMille"": 300,
                ""inheritedStats"": [""MaxHp"", ""Attack"", ""Magic"", ""Defense"", ""Resist""],
                ""focuses"": {},
                ""requirements"": []
            }
        }";

        private const string Stages = @"{
            ""rewardTables"": [
                {
                    ""id"": ""rew_test_gold"",
                    ""entries"": [ { ""kind"": ""Gold"", ""weight"": 1, ""minAmount"": 77, ""maxAmount"": 77 } ]
                },
                {
                    ""id"": ""rew_test_boss_clear"",
                    ""entries"": [
                        { ""kind"": ""Material"", ""weight"": 1, ""refId"": ""mat_test_ore"",
                          ""minAmount"": 5, ""maxAmount"": 5 }
                    ]
                },
                {
                    ""id"": ""rew_test_ranged"",
                    ""entries"": [ { ""kind"": ""Gold"", ""weight"": 1, ""minAmount"": 1, ""maxAmount"": 1000 } ]
                }
            ],
            ""events"": [
                {
                    ""id"": ""evt_test_buff"",
                    ""name"": ""Test Shrine"",
                    ""outcomes"": [
                        { ""kind"": ""GrantBuffSkill"", ""weight"": 1, ""skillId"": ""skl_test_relic_defense"" }
                    ]
                }
            ],
            ""stages"": [
                {
                    ""id"": ""stg_test_linear_win"",
                    ""name"": ""Linear win"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""nodeRewardTableId"": ""rew_test_gold"",
                    ""clearRewardTableId"": ""rew_test_boss_clear"",
                    ""bossEncounterId"": ""enc_test_weak"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_weak""] },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_weak""] },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_weak""] }
                    ]
                },
                {
                    ""id"": ""stg_test_lose"",
                    ""name"": ""Guaranteed loss"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""keepRewardsOnDefeat"": true,
                    ""bossEncounterId"": ""enc_test_strong"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Treasure"": 1 }, ""treasureTableId"": ""rew_test_gold"" },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_strong""] }
                    ]
                },
                {
                    ""id"": ""stg_test_lose_no_keep"",
                    ""name"": ""Guaranteed loss, rewards wiped"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""keepRewardsOnDefeat"": false,
                    ""bossEncounterId"": ""enc_test_strong"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Treasure"": 1 }, ""treasureTableId"": ""rew_test_gold"" },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_strong""] }
                    ]
                },
                {
                    ""id"": ""stg_test_event_buff"",
                    ""name"": ""Guaranteed buff event"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""bossEncounterId"": ""enc_test_weak"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Event"": 1 }, ""eventIds"": [""evt_test_buff""] }
                    ]
                },
                {
                    ""id"": ""stg_test_rest"",
                    ""name"": ""Rest between fights"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""restHealPerMille"": 500,
                    ""bossEncounterId"": ""enc_test_weak"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_weak""] },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } },
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_test_weak""] }
                    ]
                },
                {
                    ""id"": ""stg_test_reward_range"",
                    ""name"": ""Reward range"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 0,
                    ""bossEncounterId"": ""enc_test_weak"",
                    ""layers"": [
                        { ""nodeCount"": 1, ""nodeWeights"": { ""Treasure"": 1 }, ""treasureTableId"": ""rew_test_ranged"" }
                    ]
                },
                {
                    ""id"": ""stg_test_map"",
                    ""name"": ""Map generation shape"",
                    ""recommendedLevel"": 1,
                    ""branchChancePerMille"": 500,
                    ""bossEncounterId"": ""enc_test_weak"",
                    ""layers"": [
                        {
                            ""nodeCount"": 3,
                            ""nodeWeights"": { ""Battle"": 2, ""Event"": 1, ""Rest"": 1 },
                            ""encounterIds"": [""enc_test_weak""],
                            ""eventIds"": [""evt_test_buff""]
                        },
                        {
                            ""nodeCount"": 2,
                            ""nodeWeights"": { ""Battle"": 1, ""Treasure"": 1 },
                            ""encounterIds"": [""enc_test_weak""],
                            ""treasureTableId"": ""rew_test_gold""
                        }
                    ]
                }
            ]
        }";

        /// <summary>A single-slot placement of the standard test hero, ready for
        /// <see cref="ExpeditionRun.Start"/>.</summary>
        public static List<KeyValuePair<int, OwnedCharacter>> HeroPlacement()
        {
            return new List<KeyValuePair<int, OwnedCharacter>>
            {
                new KeyValuePair<int, OwnedCharacter>(0, new OwnedCharacter
                {
                    InstanceId = HeroInstanceId,
                    OwnerId = "test_player",
                    CharacterId = HeroCharacterId,
                    Level = 1,
                    ContentVersion = "test.1"
                })
            };
        }
    }
}
