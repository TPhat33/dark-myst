using System.Collections.Generic;
using DarkMyst.Combat.Model;

namespace DarkMyst.Content
{
    /// <summary>
    /// Turns stored progression into the numbers a battle uses.
    /// <para>
    /// This runs on the server when it resolves a real battle, and in the client when it shows
    /// a stat sheet or an evolve preview. Sharing one implementation is the reason the backend
    /// is C#: a preview that disagrees with the result is the fastest way to lose a player's
    /// trust in a system built on grinding.
    /// </para>
    /// </summary>
    public static class Progression
    {
        /// <summary>
        /// <c>stat(level) = base + growth * (level - 1)</c>, then the evolve bonus and the
        /// focus modifier, both in per-mille, applied together so their order cannot matter.
        /// </summary>
        public static StatBlock ComputeStats(
            ContentPack pack, CharacterData character, int level, EvolveFocus focus, int inheritedBonusPerMille)
        {
            int cap = pack.Progression.MaxLevelForStage(character.EvolveStage);
            if (level < 1)
            {
                level = 1;
            }
            else if (level > cap)
            {
                level = cap;
            }

            EvolveRulesData rules = pack.Progression.Evolve;
            var inherited = new HashSet<Stat>(rules.InheritedStats);

            Dictionary<Stat, int> focusDeltas;
            if (focus == EvolveFocus.None || !rules.Focuses.TryGetValue(focus, out focusDeltas))
            {
                focusDeltas = null;
            }

            int bonus = inheritedBonusPerMille;
            if (bonus < 0)
            {
                bonus = 0;
            }
            else if (bonus > rules.InheritedBonusCapPerMille)
            {
                bonus = rules.InheritedBonusCapPerMille;
            }

            StatBlock baseStats = character.BaseStats.ToStatBlock();
            StatBlock growth = character.GrowthPerLevel.ToStatBlock();
            var result = new StatBlock();

            foreach (Stat stat in StatBlock.AllStats)
            {
                long value = baseStats.Get(stat) + (long)growth.Get(stat) * (level - 1);

                int modifier = inherited.Contains(stat) ? bonus : 0;
                int focusDelta;
                if (focusDeltas != null && focusDeltas.TryGetValue(stat, out focusDelta))
                {
                    modifier += focusDelta;
                }

                if (modifier != 0)
                {
                    value = value * (1000L + modifier) / 1000L;
                }

                if (value < 0L)
                {
                    value = 0L;
                }

                result.Set(stat, value > int.MaxValue ? int.MaxValue : (int)value);
            }

            if (result.MaxHp < 1)
            {
                result.MaxHp = 1;
            }

            return result;
        }

        public static StatBlock ComputeStats(ContentPack pack, OwnedCharacter owned)
        {
            CharacterData character = pack.GetCharacter(owned.CharacterId);
            return ComputeStats(pack, character, owned.Level, owned.Focus, owned.InheritedBonusPerMille);
        }

        /// <summary>Builds the battle-ready snapshot of an owned character for a given slot.</summary>
        public static UnitDefinition BuildUnit(ContentPack pack, OwnedCharacter owned, int slot)
        {
            CharacterData character = pack.GetCharacter(owned.CharacterId);
            return new UnitDefinition
            {
                InstanceId = owned.InstanceId,
                CharacterId = character.Id,
                DisplayName = character.Name,
                Affinity = character.Affinity,
                Slot = slot,
                Stats = ComputeStats(pack, owned),
                Skills = pack.ResolveSkills(character.SkillIds)
            };
        }

        /// <summary>Builds a team from owned characters placed in explicit slots.</summary>
        public static TeamDefinition BuildTeam(
            ContentPack pack, string teamId, int leaderSlot, IEnumerable<KeyValuePair<int, OwnedCharacter>> placements)
        {
            var team = new TeamDefinition { TeamId = teamId, LeaderSlot = leaderSlot };
            foreach (KeyValuePair<int, OwnedCharacter> placement in placements)
            {
                team.Units.Add(BuildUnit(pack, placement.Value, placement.Key));
            }

            return team;
        }

        /// <summary>Builds the enemy team for an authored encounter.</summary>
        public static TeamDefinition BuildEncounterTeam(ContentPack pack, string encounterId)
        {
            EncounterData encounter = pack.GetEncounter(encounterId);
            var team = new TeamDefinition { TeamId = encounter.Id, LeaderSlot = encounter.LeaderSlot };

            foreach (EncounterUnitData placement in encounter.Units)
            {
                EnemyData enemy = pack.GetEnemy(placement.EnemyId);
                StatBlock stats = enemy.Stats.ToStatBlock();

                if (placement.StatScalePerMille != 1000)
                {
                    foreach (Stat stat in StatBlock.AllStats)
                    {
                        long scaled = (long)stats.Get(stat) * placement.StatScalePerMille / 1000L;
                        stats.Set(stat, scaled > int.MaxValue ? int.MaxValue : (int)scaled);
                    }

                    if (stats.MaxHp < 1)
                    {
                        stats.MaxHp = 1;
                    }
                }

                team.Units.Add(new UnitDefinition
                {
                    InstanceId = encounter.Id + "#" + placement.Slot,
                    CharacterId = enemy.Id,
                    DisplayName = enemy.Name,
                    Affinity = enemy.Affinity,
                    Slot = placement.Slot,
                    Stats = stats,
                    Skills = pack.ResolveSkills(enemy.SkillIds)
                });
            }

            return team;
        }
    }
}
