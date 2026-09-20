using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Content.Tests
{
    public class ContentPackTests
    {
        [Fact]
        public void The_shipped_content_pack_loads_and_validates()
        {
            ContentPack pack = ShippedContent.Instance;

            Assert.Equal("0.1.0", pack.Version);
            Assert.Equal(CombatRules.Version, pack.Manifest.RulesVersion);
            Assert.NotEmpty(pack.Characters);
            Assert.NotEmpty(pack.Enemies);
            Assert.NotEmpty(pack.Encounters);
        }

        [Fact]
        public void Every_authored_encounter_produces_a_runnable_battle()
        {
            ContentPack pack = ShippedContent.Instance;
            TeamDefinition roster = SampleTeam(pack);

            foreach (EncounterData encounter in pack.Encounters)
            {
                TeamDefinition enemies = Progression.BuildEncounterTeam(pack, encounter.Id);

                BattleResult result = BattleSimulator.Run(new BattleRequest
                {
                    Seed = 4242,
                    ContentVersion = pack.Version,
                    Attacker = roster,
                    Defender = enemies
                });

                Assert.NotEmpty(result.Events);
                Assert.Equal(BattleEventKind.BattleStarted, result.Events[0].Kind);
                Assert.Equal(BattleEventKind.BattleEnded, result.Events[result.Events.Count - 1].Kind);
                Assert.InRange(result.Rounds, 1, 30);
            }
        }

        [Fact]
        public void An_encounter_replays_to_the_same_checksum()
        {
            ContentPack pack = ShippedContent.Instance;

            BattleResult Run() => BattleSimulator.Run(new BattleRequest
            {
                Seed = 777,
                ContentVersion = pack.Version,
                Attacker = SampleTeam(pack),
                Defender = Progression.BuildEncounterTeam(pack, "enc_boss_ashen_revenant")
            });

            Assert.Equal(Run().Checksum, Run().Checksum);
        }

        [Fact]
        public void Encounter_stat_scaling_multiplies_every_stat()
        {
            ContentPack pack = ShippedContent.Instance;
            TeamDefinition team = Progression.BuildEncounterTeam(pack, "enc_crypt_patrol");

            UnitDefinition plain = team.Units.Find(u => u.Slot == 2);   // scale 1000
            UnitDefinition scaled = team.Units.Find(u => u.Slot == 4);  // scale 1200

            Assert.Equal(plain.CharacterId, scaled.CharacterId);
            Assert.Equal(plain.Stats.MaxHp * 12 / 10, scaled.Stats.MaxHp);
            Assert.Equal(plain.Stats.Attack * 12 / 10, scaled.Stats.Attack);
        }

        [Fact]
        public void Every_character_line_has_a_reachable_final_stage()
        {
            ContentPack pack = ShippedContent.Instance;

            foreach (CharacterData character in pack.Characters)
            {
                var visited = new HashSet<string>();
                CharacterData current = character;

                while (!string.IsNullOrEmpty(current.NextStageId))
                {
                    Assert.True(
                        visited.Add(current.Id),
                        "Evolve chain starting at " + character.Id + " loops back on itself.");

                    current = pack.GetCharacter(current.NextStageId);
                    Assert.Equal(character.LineId, current.LineId);
                }
            }
        }

        [Fact]
        public void Stats_grow_with_level_and_stop_at_the_stage_cap()
        {
            ContentPack pack = ShippedContent.Instance;
            CharacterData knight = pack.GetCharacter("chr_ashen_knight_i");

            StatBlock atOne = Progression.ComputeStats(pack, knight, 1, EvolveFocus.None, 0);
            StatBlock atTwenty = Progression.ComputeStats(pack, knight, 20, EvolveFocus.None, 0);
            StatBlock beyondCap = Progression.ComputeStats(pack, knight, 999, EvolveFocus.None, 0);

            Assert.Equal(knight.BaseStats.MaxHp, atOne.MaxHp);
            Assert.Equal(knight.BaseStats.MaxHp + (knight.GrowthPerLevel.MaxHp * 19), atTwenty.MaxHp);
            Assert.Equal(atTwenty, beyondCap);
        }

        [Fact]
        public void A_focus_trades_one_stat_away_for_two_others()
        {
            ContentPack pack = ShippedContent.Instance;
            CharacterData knight = pack.GetCharacter("chr_ashen_knight_i");

            StatBlock plain = Progression.ComputeStats(pack, knight, 10, EvolveFocus.None, 0);
            StatBlock offense = Progression.ComputeStats(pack, knight, 10, EvolveFocus.Offense, 0);

            Assert.True(offense.Attack > plain.Attack);
            Assert.True(offense.Defense < plain.Defense);
            Assert.Equal(plain.Speed, offense.Speed);
        }

        [Fact]
        public void The_inherited_bonus_lifts_only_the_stats_content_lists()
        {
            ContentPack pack = ShippedContent.Instance;
            CharacterData knight = pack.GetCharacter("chr_ashen_knight_i");

            StatBlock plain = Progression.ComputeStats(pack, knight, 10, EvolveFocus.None, 0);
            StatBlock boosted = Progression.ComputeStats(pack, knight, 10, EvolveFocus.None, 200);

            Assert.Equal(plain.MaxHp * 12 / 10, boosted.MaxHp);
            Assert.Equal(plain.Attack * 12 / 10, boosted.Attack);

            // Speed and crit stay out of it so speed tiers remain a design lever.
            Assert.Equal(plain.Speed, boosted.Speed);
            Assert.Equal(plain.CritRate, boosted.CritRate);
        }

        [Fact]
        public void The_inherited_bonus_cannot_exceed_its_cap()
        {
            ContentPack pack = ShippedContent.Instance;
            CharacterData knight = pack.GetCharacter("chr_ashen_knight_i");

            StatBlock atCap = Progression.ComputeStats(pack, knight, 10, EvolveFocus.None, 300);
            StatBlock wayOver = Progression.ComputeStats(pack, knight, 10, EvolveFocus.None, 5000);

            Assert.Equal(atCap, wayOver);
        }

        private static TeamDefinition SampleTeam(ContentPack pack)
        {
            var placements = new List<KeyValuePair<int, OwnedCharacter>>
            {
                Place(0, "chr_ashen_knight_i", 20),
                Place(1, "chr_grave_warden_i", 20),
                Place(2, "chr_ember_adept_i", 20),
                Place(3, "chr_tide_oracle_i", 20),
                Place(4, "chr_pale_stalker_i", 20)
            };

            return Progression.BuildTeam(pack, "test_roster", 0, placements);
        }

        private static KeyValuePair<int, OwnedCharacter> Place(int slot, string characterId, int level)
        {
            return new KeyValuePair<int, OwnedCharacter>(slot, new OwnedCharacter
            {
                InstanceId = "own_" + slot,
                OwnerId = "player_1",
                CharacterId = characterId,
                Level = level,
                ContentVersion = "0.1.0"
            });
        }
    }
}
