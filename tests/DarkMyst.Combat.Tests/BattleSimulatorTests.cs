using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Combat.Tests
{
    public class BattleSimulatorTests
    {
        /// <summary>Variance pinned to 1.0 so a test can assert an exact number.</summary>
        private static CombatRules NoVariance()
        {
            CombatRules rules = CombatRules.Default;
            rules.VarianceMinPerMille = 1000;
            rules.VarianceMaxPerMille = 1000;
            return rules;
        }

        private static SkillDefinition SelfTaunt(int durationRounds = 99)
        {
            return new SkillDefinition
            {
                Id = "skl_test_taunt",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Taunt,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_taunt",
                        DurationRounds = durationRounds
                    }
                }
            };
        }

        // ------------------------------------------------------------------
        // Turn order
        // ------------------------------------------------------------------

        [Fact]
        public void Turn_order_follows_speed_then_side_then_slot()
        {
            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("slow", 0, TestUnits.Stats(speed: 80, maxHp: 99999), new[] { TestUnits.Idle() }),
                TestUnits.Unit("fast", 1, TestUnits.Stats(speed: 150, maxHp: 99999), new[] { TestUnits.Idle() }));

            var defender = TestUnits.Team(
                "d",
                // Same speed as "slow": the attacker side breaks the tie.
                TestUnits.Unit("tie", 0, TestUnits.Stats(speed: 80, maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> turns = TestUnits.EventsOfKind(result, BattleEventKind.TurnStarted);
            Assert.Equal(new UnitRef(TeamSide.Attacker, 1), turns[0].Source.Value);
            Assert.Equal(new UnitRef(TeamSide.Attacker, 0), turns[1].Source.Value);
            Assert.Equal(new UnitRef(TeamSide.Defender, 0), turns[2].Source.Value);
        }

        [Fact]
        public void A_speed_debuff_changes_the_order_from_the_next_round()
        {
            var slowDown = new SkillDefinition
            {
                Id = "skl_test_slow",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.StatModifier,
                        Target = TargetSelector.AllEnemies,
                        StatusId = "st_test_slow",
                        ModifiedStat = Stat.Speed,
                        AmountPerMille = -500,
                        DurationRounds = 99
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("hexer", 0, TestUnits.Stats(speed: 100, maxHp: 99999),
                    new[] { slowDown, TestUnits.Idle() }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("victim", 0, TestUnits.Stats(speed: 140, maxHp: 99999),
                    new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            // 140 * 0.5 = 70, below the attacker's 100.
            List<BattleEvent> turns = TestUnits.EventsOfKind(result, BattleEventKind.TurnStarted);
            Assert.Equal(TeamSide.Attacker, turns[0].Source.Value.Side);
        }

        // ------------------------------------------------------------------
        // Positioning
        // ------------------------------------------------------------------

        [Fact]
        public void The_back_row_takes_less_physical_damage_than_the_front_row()
        {
            int Damage(int defenderSlot)
            {
                var attacker = TestUnits.Team(
                    "a",
                    TestUnits.Unit("hitter", 0, TestUnits.Stats(attack: 300, speed: 200, maxHp: 99999)));

                var defender = TestUnits.Team(
                    "d",
                    TestUnits.Unit("target", defenderSlot, TestUnits.Stats(maxHp: 99999, defense: 300),
                        new[] { TestUnits.Idle() }));

                BattleResult result = BattleSimulator.Run(
                    TestUnits.Battle(attacker, defender, rules: NoVariance()));

                return TestUnits.EventsOfKind(result, BattleEventKind.Damaged)[0].Amount;
            }

            int front = Damage(0);
            int back = Damage(2);

            Assert.Equal(165, front); // 300 * 0.5 mitigation * 1.1 row
            Assert.Equal(135, back);  // 300 * 0.5 mitigation * 0.9 row
        }

        [Fact]
        public void Magical_damage_ignores_the_row()
        {
            var spell = new SkillDefinition
            {
                Id = "skl_test_spell",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Damage,
                        Target = TargetSelector.RandomEnemy,
                        DamageKind = DamageKind.Magical,
                        PowerPerMille = 1000,
                        CanCrit = false
                    }
                }
            };

            int Damage(int defenderSlot)
            {
                var attacker = TestUnits.Team(
                    "a", TestUnits.Unit("caster", 0, TestUnits.Stats(magic: 300, speed: 200), new[] { spell }));
                var defender = TestUnits.Team(
                    "d",
                    TestUnits.Unit("target", defenderSlot, TestUnits.Stats(maxHp: 99999, resist: 300),
                        new[] { TestUnits.Idle() }));

                BattleResult result = BattleSimulator.Run(
                    TestUnits.Battle(attacker, defender, rules: NoVariance()));
                return TestUnits.EventsOfKind(result, BattleEventKind.Damaged)[0].Amount;
            }

            Assert.Equal(Damage(0), Damage(4));
        }

        // ------------------------------------------------------------------
        // Targeting
        // ------------------------------------------------------------------

        [Fact]
        public void A_taunting_unit_soaks_every_single_target_attack()
        {
            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("hitter", 0, TestUnits.Stats(attack: 60, speed: 200)));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("plain", 0, TestUnits.Stats(maxHp: 99999), new[] { TestUnits.Idle() }),
                TestUnits.Unit("taunter", 1, TestUnits.Stats(maxHp: 99999),
                    new[] { SelfTaunt(), TestUnits.Idle() }),
                TestUnits.Unit("backline", 3, TestUnits.Stats(maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> hits = TestUnits.EventsOfKind(result, BattleEventKind.Damaged);
            Assert.NotEmpty(hits);
            foreach (BattleEvent hit in hits)
            {
                Assert.Equal(new UnitRef(TeamSide.Defender, 1), hit.Target.Value);
            }
        }

        [Fact]
        public void An_attack_flagged_ignoresTaunt_reaches_past_the_taunter()
        {
            var sniper = new SkillDefinition
            {
                Id = "skl_test_snipe",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Damage,
                        Target = TargetSelector.LowestHpEnemy,
                        DamageKind = DamageKind.Physical,
                        PowerPerMille = 100,
                        IgnoresTaunt = true,
                        CanCrit = false
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("sniper", 0, TestUnits.Stats(attack: 60, speed: 200), new[] { sniper }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("taunter", 0, TestUnits.Stats(maxHp: 99999),
                    new[] { SelfTaunt(), TestUnits.Idle() }),
                TestUnits.Unit("squishy", 3, TestUnits.Stats(maxHp: 500), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.Equal(
                new UnitRef(TeamSide.Defender, 3),
                TestUnits.EventsOfKind(result, BattleEventKind.Damaged)[0].Target.Value);
        }

        [Fact]
        public void A_front_row_attack_falls_through_to_the_back_once_the_front_is_gone()
        {
            var frontStrike = new SkillDefinition
            {
                Id = "skl_test_front",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Damage,
                        Target = TargetSelector.FrontRowEnemy,
                        DamageKind = DamageKind.Physical,
                        PowerPerMille = 20000,
                        CanCrit = false
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("smasher", 0, TestUnits.Stats(attack: 500, speed: 200), new[] { frontStrike }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("wall", 0, TestUnits.Stats(maxHp: 100), new[] { TestUnits.Idle() }),
                TestUnits.Unit("archer", 3, TestUnits.Stats(maxHp: 100), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.Equal(BattleOutcome.AttackerVictory, result.Outcome);
            Assert.Equal(BattleEndReason.DefenderWiped, result.EndReason);
        }

        // ------------------------------------------------------------------
        // Statuses
        // ------------------------------------------------------------------

        [Fact]
        public void A_shield_absorbs_before_hp_and_is_reported_separately()
        {
            var shieldSkill = new SkillDefinition
            {
                Id = "skl_test_shield",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Shield,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_shield",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 1000,
                        DurationRounds = 99
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("hitter", 0, TestUnits.Stats(attack: 300, speed: 200)));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("warded", 0, TestUnits.Stats(maxHp: 5000, defense: 300, magic: 1000),
                    new[] { shieldSkill, TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> absorbs = TestUnits.EventsOfKind(result, BattleEventKind.ShieldAbsorbed);
            Assert.NotEmpty(absorbs);

            // First hit is 165 and the shield holds 1000, so nothing reaches HP.
            Assert.Equal(165, absorbs[0].Amount);
            Assert.Equal(5000, absorbs[0].TargetHpAfter);
        }

        [Fact]
        public void Poison_ticks_at_round_end_and_can_be_lethal()
        {
            var poison = new SkillDefinition
            {
                Id = "skl_test_poison",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.DamageOverTime,
                        Target = TargetSelector.AllEnemies,
                        StatusId = "st_test_poison",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 1000,
                        DurationRounds = 5
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("hexer", 0, TestUnits.Stats(magic: 400, speed: 200, maxHp: 99999),
                    new[] { poison, TestUnits.Idle() }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("victim", 0, TestUnits.Stats(maxHp: 700, defense: 99999),
                    new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> ticks = TestUnits.EventsOfKind(result, BattleEventKind.StatusTicked);
            Assert.Equal(400, ticks[0].Amount);
            Assert.Equal(300, ticks[0].TargetHpAfter);

            // Second tick finishes the job, so the battle ends on round 2.
            Assert.Equal(BattleOutcome.AttackerVictory, result.Outcome);
            Assert.Equal(2, result.Rounds);
        }

        [Fact]
        public void Poison_stacks_up_to_its_limit_and_no_further()
        {
            var poison = new SkillDefinition
            {
                Id = "skl_test_poison_stack",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.DamageOverTime,
                        Target = TargetSelector.AllEnemies,
                        StatusId = "st_test_poison",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 100,
                        DurationRounds = 9,
                        MaxStacks = 2,
                        StackRule = StackRule.Stack
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("hexer", 0, TestUnits.Stats(magic: 1000, speed: 200, maxHp: 99999),
                    new[] { poison }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("victim", 0, TestUnits.Stats(maxHp: 99999, defense: 99999),
                    new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> ticks = TestUnits.EventsOfKind(result, BattleEventKind.StatusTicked);
            Assert.Equal(100, ticks[0].Amount); // 1 stack
            Assert.Equal(200, ticks[1].Amount); // 2 stacks
            Assert.Equal(200, ticks[2].Amount); // capped at MaxStacks
        }

        [Fact]
        public void Regeneration_resolves_before_poison_within_the_same_round()
        {
            var regen = new SkillDefinition
            {
                Id = "skl_test_regen",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.HealOverTime,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_regen",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 1000,
                        DurationRounds = 9
                    },
                    new SkillEffect
                    {
                        Kind = EffectKind.DamageOverTime,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_selfpoison",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 1200,
                        DurationRounds = 9
                    }
                }
            };

            // 100 HP, damaged to near nothing, then 200 regen and 240 poison per round.
            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("martyr", 0, TestUnits.Stats(maxHp: 300, magic: 200, speed: 200, defense: 99999),
                    new[] { regen, TestUnits.Idle() }));

            var defender = TestUnits.Team(
                "d", TestUnits.Unit("dummy", 0, TestUnits.Stats(maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> ticks = TestUnits.EventsOfKind(result, BattleEventKind.StatusTicked);

            // Round 1: already at full HP so regen heals nothing, then poison takes 240.
            Assert.Equal("st_test_selfpoison", ticks[0].StatusId);
            Assert.Equal(60, ticks[0].TargetHpAfter);

            // Round 2: regen first (60 -> 260), then poison (260 -> 20). Order kept it alive.
            Assert.Equal("st_test_regen", ticks[1].StatusId);
            Assert.Equal(260, ticks[1].TargetHpAfter);
            Assert.Equal("st_test_selfpoison", ticks[2].StatusId);
            Assert.Equal(20, ticks[2].TargetHpAfter);
        }

        [Fact]
        public void A_stun_costs_one_turn_and_then_grants_immunity()
        {
            var stun = new SkillDefinition
            {
                Id = "skl_test_stun",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Stun,
                        Target = TargetSelector.AllEnemies,
                        StatusId = "st_test_stun",
                        DurationRounds = 1
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("stunner", 0, TestUnits.Stats(speed: 200, maxHp: 99999), new[] { stun }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("victim", 0, TestUnits.Stats(speed: 10, maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.NotEmpty(TestUnits.EventsOfKind(result, BattleEventKind.TurnSkipped));

            // The second application is refused outright, so a pair of stunners cannot
            // lock a unit out of the whole battle.
            List<BattleEvent> resisted = TestUnits.EventsOfKind(result, BattleEventKind.StatusResisted);
            Assert.Contains(resisted, e => e.Note == "stun immune");
        }

        [Fact]
        public void Cleanse_removes_debuffs_and_leaves_buffs_alone()
        {
            var setup = new SkillDefinition
            {
                Id = "skl_test_setup",
                Trigger = TriggerKind.OnBattleStart,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.StatModifier,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_buff",
                        ModifiedStat = Stat.Attack,
                        AmountPerMille = 200,
                        DurationRounds = 99
                    },
                    new SkillEffect
                    {
                        Kind = EffectKind.StatModifier,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_debuff",
                        ModifiedStat = Stat.Defense,
                        AmountPerMille = -200,
                        DurationRounds = 99
                    }
                }
            };

            var cleanse = new SkillDefinition
            {
                Id = "skl_test_cleanse",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Cleanse,
                        Target = TargetSelector.Self,
                        RemoveCount = 5
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("cleanser", 0, TestUnits.Stats(speed: 200, maxHp: 99999),
                    new[] { setup, cleanse }));

            var defender = TestUnits.Team(
                "d", TestUnits.Unit("dummy", 0, TestUnits.Stats(maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> removed = TestUnits.EventsOfKind(result, BattleEventKind.StatusRemoved);
            Assert.Contains(removed, e => e.StatusId == "st_test_debuff");
            Assert.DoesNotContain(removed, e => e.StatusId == "st_test_buff");
        }

        // ------------------------------------------------------------------
        // Death and revival
        // ------------------------------------------------------------------

        [Fact]
        public void A_revive_brings_an_ally_back_at_the_stated_share_of_max_hp()
        {
            var revive = new SkillDefinition
            {
                Id = "skl_test_revive",
                Trigger = TriggerKind.OnAllyDown,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Revive,
                        Target = TargetSelector.LowestSlotDownedAlly,
                        AmountPerMille = 400
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("executioner", 0, TestUnits.Stats(attack: 5000, speed: 200)));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("doomed", 0, TestUnits.Stats(maxHp: 1000, defense: 0), new[] { TestUnits.Idle() }),
                TestUnits.Unit("cantor", 3, TestUnits.Stats(maxHp: 99999), new[] { revive, TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            List<BattleEvent> revives = TestUnits.EventsOfKind(result, BattleEventKind.UnitRevived);
            Assert.NotEmpty(revives);
            Assert.Equal(400, revives[0].Amount);
            Assert.Equal(new UnitRef(TeamSide.Defender, 0), revives[0].Target.Value);
        }

        [Fact]
        public void A_death_rattle_still_fires_for_the_unit_that_just_went_down()
        {
            var rattle = new SkillDefinition
            {
                Id = "skl_test_rattle",
                Trigger = TriggerKind.OnDeath,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.DamageOverTime,
                        Target = TargetSelector.AllEnemies,
                        StatusId = "st_test_curse",
                        ScalingStat = Stat.Magic,
                        AmountPerMille = 1000,
                        DurationRounds = 3
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("executioner", 0, TestUnits.Stats(attack: 5000, speed: 200, maxHp: 99999)));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("cursed", 0, TestUnits.Stats(maxHp: 100, magic: 300, defense: 0),
                    new[] { rattle, TestUnits.Idle() }),
                TestUnits.Unit("other", 1, TestUnits.Stats(maxHp: 99999), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.Contains(
                TestUnits.EventsOfKind(result, BattleEventKind.StatusApplied),
                e => e.StatusId == "st_test_curse");
        }

        [Fact]
        public void Two_units_that_both_counter_on_being_hit_still_terminate()
        {
            var counter = new SkillDefinition
            {
                Id = "skl_test_counter",
                Trigger = TriggerKind.OnAfterDamaged,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Damage,
                        Target = TargetSelector.TriggerSource,
                        DamageKind = DamageKind.True,
                        ScalingStat = Stat.Attack,
                        PowerPerMille = 100,
                        CanCrit = false
                    }
                }
            };

            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("a0", 0, TestUnits.Stats(maxHp: 99999, attack: 100, speed: 120),
                    new[] { TestUnits.BasicAttack(), counter }));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("d0", 0, TestUnits.Stats(maxHp: 99999, attack: 100, speed: 110),
                    new[] { TestUnits.BasicAttack(), counter }));

            // The real assertion is that this returns at all: the reaction depth cap is what
            // stops counter-attacks bouncing forever.
            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.Equal(BattleEndReason.RoundLimit, result.EndReason);
        }

        // ------------------------------------------------------------------
        // Resolution
        // ------------------------------------------------------------------

        [Fact]
        public void The_round_limit_is_decided_on_remaining_hp_share()
        {
            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("a0", 0, TestUnits.Stats(maxHp: 99999, attack: 200, speed: 120, defense: 300)));

            var defender = TestUnits.Team(
                "d",
                TestUnits.Unit("d0", 0, TestUnits.Stats(maxHp: 99999, attack: 50, speed: 110, defense: 300)));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            Assert.Equal(BattleEndReason.RoundLimit, result.EndReason);
            Assert.Equal(BattleOutcome.AttackerVictory, result.Outcome);
            Assert.Equal(30, result.Rounds);
        }

        [Fact]
        public void A_perfectly_symmetric_stalemate_is_a_draw()
        {
            UnitDefinition Passive(string id) =>
                TestUnits.Unit(id, 0, TestUnits.Stats(maxHp: 5000), new[] { TestUnits.Idle() });

            BattleResult result = BattleSimulator.Run(
                TestUnits.Battle(
                    TestUnits.Team("a", Passive("a0")),
                    TestUnits.Team("d", Passive("d0")),
                    rules: NoVariance()));

            Assert.Equal(BattleOutcome.Draw, result.Outcome);
            Assert.Equal(BattleEndReason.RoundLimit, result.EndReason);
        }

        [Fact]
        public void The_final_snapshot_matches_the_log()
        {
            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("a0", 0, TestUnits.Stats(attack: 5000, speed: 200, maxHp: 99999)));
            var defender = TestUnits.Team(
                "d", TestUnits.Unit("d0", 0, TestUnits.Stats(maxHp: 100, defense: 0), new[] { TestUnits.Idle() }));

            BattleResult result = BattleSimulator.Run(TestUnits.Battle(attacker, defender, rules: NoVariance()));

            UnitSnapshot downed = result.FinalUnits.Find(u => u.InstanceId == "d0");
            Assert.False(downed.Alive);
            Assert.Equal(0, downed.Hp);
            Assert.Equal(BattleOutcome.AttackerVictory, result.Outcome);
            Assert.Equal(CombatRules.Version, result.RulesVersion);
            Assert.Equal("test", result.ContentVersion);
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        [Fact]
        public void A_unit_with_no_guaranteed_turn_action_is_rejected()
        {
            var unreliable = new SkillDefinition
            {
                Id = "skl_test_unreliable",
                Trigger = TriggerKind.OnAction,
                ActivationChancePerMille = 500,
                Effects = new List<SkillEffect> { new SkillEffect() }
            };

            var attacker = TestUnits.Team(
                "a", TestUnits.Unit("a0", 0, TestUnits.Stats(), new[] { unreliable }));
            var defender = TestUnits.Team("d", TestUnits.Unit("d0", 0, TestUnits.Stats()));

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => BattleSimulator.Run(TestUnits.Battle(attacker, defender)));

            Assert.Contains("guaranteed OnAction skill", error.Message);
        }

        [Fact]
        public void Two_units_in_the_same_slot_are_rejected()
        {
            var attacker = TestUnits.Team(
                "a",
                TestUnits.Unit("a0", 1, TestUnits.Stats()),
                TestUnits.Unit("a1", 1, TestUnits.Stats()));
            var defender = TestUnits.Team("d", TestUnits.Unit("d0", 0, TestUnits.Stats()));

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => BattleSimulator.Run(TestUnits.Battle(attacker, defender)));

            Assert.Contains("used twice", error.Message);
        }

        [Fact]
        public void An_empty_leader_slot_is_rejected()
        {
            var attacker = TestUnits.Team("a", TestUnits.Unit("a0", 0, TestUnits.Stats()));
            attacker.LeaderSlot = 4;

            var defender = TestUnits.Team("d", TestUnits.Unit("d0", 0, TestUnits.Stats()));

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => BattleSimulator.Run(TestUnits.Battle(attacker, defender)));

            Assert.Contains("leader slot", error.Message);
        }

        [Fact]
        public void A_battle_recorded_under_another_rule_set_is_refused_rather_than_mis_replayed()
        {
            BattleRequest request = TestUnits.Battle(
                TestUnits.Team("a", TestUnits.Unit("a0", 0, TestUnits.Stats())),
                TestUnits.Team("d", TestUnits.Unit("d0", 0, TestUnits.Stats())));
            request.RulesVersion = "0.9.0";

            ArgumentException error = Assert.Throws<ArgumentException>(() => BattleSimulator.Run(request));
            Assert.Contains("0.9.0", error.Message);
        }

        // ------------------------------------------------------------------
        // Leader skills
        // ------------------------------------------------------------------

        [Fact]
        public void A_leader_skill_only_applies_from_the_leader_slot()
        {
            var leaderBuff = new SkillDefinition
            {
                Id = "skl_test_leader",
                Trigger = TriggerKind.OnBattleStart,
                IsLeaderSkill = true,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.StatModifier,
                        Target = TargetSelector.AllAllies,
                        StatusId = "st_test_leader",
                        ModifiedStat = Stat.Attack,
                        AmountPerMille = 500,
                        DurationRounds = 99
                    }
                }
            };

            int FirstHit(int leaderSlot)
            {
                var attacker = TestUnits.Team(
                    "a",
                    TestUnits.Unit("captain", 0, TestUnits.Stats(attack: 200, speed: 200),
                        new[] { leaderBuff, TestUnits.BasicAttack() }),
                    TestUnits.Unit("spare", 1, TestUnits.Stats(speed: 1), new[] { TestUnits.Idle() }));
                attacker.LeaderSlot = leaderSlot;

                var defender = TestUnits.Team(
                    "d",
                    TestUnits.Unit("target", 0, TestUnits.Stats(maxHp: 99999, defense: 300),
                        new[] { TestUnits.Idle() }));

                BattleResult result = BattleSimulator.Run(
                    TestUnits.Battle(attacker, defender, rules: NoVariance()));

                return TestUnits.EventsOfKind(result, BattleEventKind.Damaged)[0].Amount;
            }

            int withLeader = FirstHit(0);    // captain is the leader
            int withoutLeader = FirstHit(1); // the spare is, so the buff never applies

            Assert.Equal(110, withoutLeader); // 200 atk * 0.5 mitigation * 1.1 row
            Assert.Equal(165, withLeader);    // +50% leader buff: 300 atk through the same pipeline
        }
    }
}
