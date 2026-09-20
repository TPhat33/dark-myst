using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using Xunit;

namespace DarkMyst.Content.Tests
{
    /// <summary>
    /// Every cross-check <c>ContentPack.Validate()</c> gained for expedition stages, reward
    /// tables and events. Each test loads a minimal pack that is broken in exactly one way, so a
    /// typo in <c>stages.json</c> fails here, at load time, rather than in a player's session —
    /// the same guarantee <c>docs/05-content-pipeline.md</c> states for the rest of the content.
    /// </summary>
    public class ExpeditionValidationTests
    {
        private const string Skills = @"{
            ""skills"": [
                { ""id"": ""skl_test_atk"", ""name"": ""Strike"", ""trigger"": ""OnAction"",
                  ""effects"": [ { ""kind"": ""Damage"", ""target"": ""RandomEnemy"" } ] },
                { ""id"": ""skl_test_on_action_buff"", ""name"": ""Wrong trigger buff"",
                  ""trigger"": ""OnAction"",
                  ""effects"": [ { ""kind"": ""StatModifier"", ""target"": ""AllAllies"",
                                    ""statusId"": ""st_x"", ""modifiedStat"": ""Defense"",
                                    ""amountPerMille"": 100, ""durationRounds"": 99 } ] },
                { ""id"": ""skl_test_leader_buff"", ""name"": ""Leader-only buff"",
                  ""trigger"": ""OnBattleStart"", ""isLeaderSkill"": true,
                  ""effects"": [ { ""kind"": ""StatModifier"", ""target"": ""AllAllies"",
                                    ""statusId"": ""st_y"", ""modifiedStat"": ""Defense"",
                                    ""amountPerMille"": 100, ""durationRounds"": 99 } ] },
                { ""id"": ""skl_test_good_buff"", ""name"": ""Good buff"",
                  ""trigger"": ""OnBattleStart"",
                  ""effects"": [ { ""kind"": ""StatModifier"", ""target"": ""AllAllies"",
                                    ""statusId"": ""st_z"", ""modifiedStat"": ""Defense"",
                                    ""amountPerMille"": 100, ""durationRounds"": 99 } ] },
                { ""id"": ""skl_test_stacking_buff"", ""name"": ""Stacking buff"",
                  ""trigger"": ""OnBattleStart"",
                  ""effects"": [ { ""kind"": ""StatModifier"", ""target"": ""AllAllies"",
                                    ""statusId"": ""st_stack"", ""modifiedStat"": ""Defense"",
                                    ""amountPerMille"": 100, ""durationRounds"": 99,
                                    ""maxStacks"": 5, ""stackRule"": ""Stack"" } ] }
            ]
        }";

        private const string Characters = @"{
            ""characters"": [
                { ""id"": ""chr_x"", ""lineId"": ""line_x"", ""name"": ""X"", ""rarity"": 1, ""evolveStage"": 1,
                  ""baseStats"": { ""maxHp"": 100 }, ""skillIds"": [""skl_test_atk""] }
            ]
        }";

        private const string Enemies = @"{
            ""enemies"": [
                { ""id"": ""enm_x"", ""name"": ""X"", ""stats"": { ""maxHp"": 100 },
                  ""skillIds"": [""skl_test_atk""] }
            ]
        }";

        private const string Encounters = @"{
            ""encounters"": [
                { ""id"": ""enc_x"", ""name"": ""X"", ""leaderSlot"": 0,
                  ""units"": [ { ""enemyId"": ""enm_x"", ""slot"": 0 } ] }
            ]
        }";

        private const string Progression = @"{
            ""maxLevelByStage"": [20],
            ""evolve"": { ""focuses"": {}, ""requirements"": [] }
        }";

        private static string Manifest()
        {
            return @"{
                ""contentVersion"": ""test"",
                ""rulesVersion"": """ + CombatRules.Version + @""",
                ""skills"": ""skills.json"", ""characters"": ""characters.json"",
                ""enemies"": ""enemies.json"", ""progression"": ""progression.json"",
                ""encounters"": ""encounters.json"", ""stages"": ""stages.json""
            }";
        }

        /// <summary>Loads a pack whose <c>stages.json</c> body is exactly <paramref name="stagesJson"/>;
        /// every other file is the same small fixed pack above.</summary>
        private static ContentException LoadInvalid(string stagesJson)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Manifest(),
                ["skills.json"] = Skills,
                ["characters.json"] = Characters,
                ["enemies.json"] = Enemies,
                ["encounters.json"] = Encounters,
                ["progression.json"] = Progression,
                ["stages.json"] = stagesJson
            };

            return Assert.Throws<ContentException>(() => ContentPack.Load(name => files[name]));
        }

        private static string StageWithOneLayer(string layerJson, string extraStageFields = "")
        {
            return @"{
                ""rewardTables"": [],
                ""events"": [],
                ""stages"": [
                    {
                        ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                        ""bossEncounterId"": ""enc_x"",
                        " + extraStageFields + @"
                        ""layers"": [ " + layerJson + @" ]
                    }
                ]
            }";
        }

        // ------------------------------------------------------------------
        // Stage-level
        // ------------------------------------------------------------------

        [Fact]
        public void A_layer_with_no_possible_node_kind_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 0 } }"));

            Assert.Contains("can never generate a node", error.Message);
        }

        [Fact]
        public void A_layer_with_a_non_positive_node_count_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 0, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_x""] }"));

            Assert.Contains("non-positive nodeCount", error.Message);
        }

        [Fact]
        public void A_layer_that_lists_a_weight_for_boss_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1, ""Boss"": 1 }, ""encounterIds"": [""enc_x""] }"));

            Assert.Contains("Boss is only ever the single node", error.Message);
        }

        [Fact]
        public void A_battle_capable_layer_with_no_encounter_pool_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 } }"));

            Assert.Contains("empty", error.Message);
        }

        [Fact]
        public void A_battle_capable_layer_referencing_an_unknown_encounter_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Battle"": 1 }, ""encounterIds"": [""enc_missing""] }"));

            Assert.Contains("enc_missing", error.Message);
        }

        [Fact]
        public void An_event_capable_layer_referencing_an_unknown_event_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Event"": 1 }, ""eventIds"": [""evt_missing""] }"));

            Assert.Contains("evt_missing", error.Message);
        }

        [Fact]
        public void A_treasure_capable_layer_with_no_treasure_table_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Treasure"": 1 } }"));

            Assert.Contains("treasureTableId", error.Message);
        }

        [Fact]
        public void A_treasure_capable_layer_referencing_an_unknown_table_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Treasure"": 1 }, ""treasureTableId"": ""rew_missing"" }"));

            Assert.Contains("rew_missing", error.Message);
        }

        [Fact]
        public void A_stage_with_no_layers_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [], ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"", ""layers"": [] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("no layers", error.Message);
        }

        [Fact]
        public void A_stage_referencing_an_unknown_boss_encounter_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [], ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_missing"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("enc_missing", error.Message);
        }

        [Fact]
        public void A_stage_with_a_non_positive_recommended_level_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [], ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 0,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("recommendedLevel", error.Message);
        }

        [Fact]
        public void A_branch_chance_outside_0_to_1000_is_rejected()
        {
            ContentException error = LoadInvalid(StageWithOneLayer(
                @"{ ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } }", @"""branchChancePerMille"": 1500,"));

            Assert.Contains("branchChancePerMille", error.Message);
        }

        // ------------------------------------------------------------------
        // Reward tables
        // ------------------------------------------------------------------

        [Fact]
        public void A_reward_table_with_no_entries_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [ { ""id"": ""rew_x"", ""entries"": [] } ],
                ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("no entries", error.Message);
        }

        [Fact]
        public void A_reward_table_dropping_an_unknown_character_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [ { ""id"": ""rew_x"", ""entries"": [
                    { ""kind"": ""Character"", ""weight"": 1, ""refId"": ""chr_missing"" } ] } ],
                ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("chr_missing", error.Message);
        }

        [Fact]
        public void A_reward_table_entry_with_min_above_max_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [ { ""id"": ""rew_x"", ""entries"": [
                    { ""kind"": ""Gold"", ""weight"": 1, ""minAmount"": 100, ""maxAmount"": 1 } ] } ],
                ""events"": [],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("minAmount exceeds maxAmount", error.Message);
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        [Fact]
        public void An_event_with_no_outcomes_is_rejected()
        {
            string json = @"{
                ""rewardTables"": [], ""events"": [ { ""id"": ""evt_x"", ""name"": ""X"", ""outcomes"": [] } ],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Rest"": 1 } } ] } ]
            }";

            ContentException error = LoadInvalid(json);
            Assert.Contains("no outcomes", error.Message);
        }

        [Fact]
        public void A_buff_grant_referencing_an_unknown_skill_is_rejected()
        {
            ContentException error = LoadInvalid(EventStage("skl_missing"));
            Assert.Contains("unknown skill", error.Message);
        }

        [Fact]
        public void A_buff_grant_whose_skill_is_not_OnBattleStart_is_rejected()
        {
            ContentException error = LoadInvalid(EventStage("skl_test_on_action_buff"));
            Assert.Contains("would never apply", error.Message);
        }

        [Fact]
        public void A_buff_grant_whose_skill_is_leader_only_is_rejected()
        {
            ContentException error = LoadInvalid(EventStage("skl_test_leader_buff"));
            Assert.Contains("leader skill", error.Message);
        }

        [Fact]
        public void A_buff_grant_whose_effect_stacks_is_rejected()
        {
            ContentException error = LoadInvalid(EventStage("skl_test_stacking_buff"));
            Assert.Contains("stackRule Stack", error.Message);
        }

        [Fact]
        public void A_well_formed_buff_grant_is_accepted()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Manifest(),
                ["skills.json"] = Skills,
                ["characters.json"] = Characters,
                ["enemies.json"] = Enemies,
                ["encounters.json"] = Encounters,
                ["progression.json"] = Progression,
                ["stages.json"] = EventStage("skl_test_good_buff")
            };

            ContentPack pack = ContentPack.Load(name => files[name]);
            Assert.NotEmpty(pack.Events);
        }

        private static string EventStage(string grantedSkillId)
        {
            return @"{
                ""rewardTables"": [],
                ""events"": [ { ""id"": ""evt_x"", ""name"": ""X"", ""outcomes"": [
                    { ""kind"": ""GrantBuffSkill"", ""weight"": 1, ""skillId"": """ + grantedSkillId + @""" }
                ] } ],
                ""stages"": [ { ""id"": ""stg_x"", ""name"": ""X"", ""recommendedLevel"": 1,
                                ""bossEncounterId"": ""enc_x"",
                                ""layers"": [ { ""nodeCount"": 1, ""nodeWeights"": { ""Event"": 1 },
                                                ""eventIds"": [""evt_x""] } ] } ]
            }";
        }
    }
}
