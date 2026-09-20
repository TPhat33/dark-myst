using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;

namespace DarkMyst.Combat.Tests
{
    /// <summary>
    /// Builders for tiny, fully specified battles. Tests read better when the setup states only
    /// the thing under test, so everything here has a boring default.
    /// </summary>
    internal static class TestUnits
    {
        public static StatBlock Stats(
            int maxHp = 1000,
            int attack = 100,
            int magic = 100,
            int defense = 100,
            int resist = 100,
            int speed = 100,
            int critRate = 0,
            int critDamage = 0)
        {
            return new StatBlock
            {
                MaxHp = maxHp,
                Attack = attack,
                Magic = magic,
                Defense = defense,
                Resist = resist,
                Speed = speed,
                CritRate = critRate,
                CritDamage = critDamage
            };
        }

        /// <summary>A guaranteed, cooldown-free physical attack. Every unit needs one.</summary>
        public static SkillDefinition BasicAttack(int powerPerMille = 1000, string id = "skl_test_basic")
        {
            return new SkillDefinition
            {
                Id = id,
                Name = "Test Strike",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.Damage,
                        Target = TargetSelector.RandomEnemy,
                        DamageKind = DamageKind.Physical,
                        PowerPerMille = powerPerMille,
                        CanCrit = false
                    }
                }
            };
        }

        /// <summary>A unit that does nothing but stand there and be hit.</summary>
        public static SkillDefinition Idle(string id = "skl_test_idle")
        {
            return new SkillDefinition
            {
                Id = id,
                Name = "Wait",
                Trigger = TriggerKind.OnAction,
                Effects = new List<SkillEffect>
                {
                    new SkillEffect
                    {
                        Kind = EffectKind.StatModifier,
                        Target = TargetSelector.Self,
                        StatusId = "st_test_noop",
                        ModifiedStat = Stat.Attack,
                        AmountPerMille = 0,
                        DurationRounds = 1,
                        StackRule = StackRule.Refresh
                    }
                }
            };
        }

        public static UnitDefinition Unit(
            string id,
            int slot,
            StatBlock stats,
            IEnumerable<SkillDefinition> skills = null,
            Affinity affinity = Affinity.Neutral)
        {
            return new UnitDefinition
            {
                InstanceId = id,
                CharacterId = id,
                DisplayName = id,
                Affinity = affinity,
                Slot = slot,
                Stats = stats,
                Skills = new List<SkillDefinition>(skills ?? new[] { BasicAttack() })
            };
        }

        public static TeamDefinition Team(string id, params UnitDefinition[] units)
        {
            var team = new TeamDefinition { TeamId = id, LeaderSlot = units.Length > 0 ? units[0].Slot : 0 };
            team.Units.AddRange(units);
            return team;
        }

        public static BattleRequest Battle(
            TeamDefinition attacker, TeamDefinition defender, ulong seed = 1, CombatRules rules = null)
        {
            return new BattleRequest
            {
                Seed = seed,
                ContentVersion = "test",
                Attacker = attacker,
                Defender = defender,
                Rules = rules
            };
        }

        /// <summary>Total of every <see cref="BattleEventKind.Damaged"/> amount in the log.</summary>
        public static int TotalDamage(BattleResult result, UnitRef? target = null)
        {
            int total = 0;
            foreach (BattleEvent evt in result.Events)
            {
                if (evt.Kind != BattleEventKind.Damaged)
                {
                    continue;
                }

                if (target.HasValue && (!evt.Target.HasValue || !evt.Target.Value.Equals(target.Value)))
                {
                    continue;
                }

                total += evt.Amount;
            }

            return total;
        }

        public static List<BattleEvent> EventsOfKind(BattleResult result, BattleEventKind kind)
        {
            var matches = new List<BattleEvent>();
            foreach (BattleEvent evt in result.Events)
            {
                if (evt.Kind == kind)
                {
                    matches.Add(evt);
                }
            }

            return matches;
        }
    }
}
