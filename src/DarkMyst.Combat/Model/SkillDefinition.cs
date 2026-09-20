using System.Collections.Generic;

namespace DarkMyst.Combat.Model
{
    /// <summary>
    /// One thing a skill does. A skill is a list of these, applied in order.
    /// <para>
    /// Adding a character that reuses existing effect kinds is a content change (JSON only).
    /// Only a genuinely new rule needs a new <see cref="EffectKind"/> and engine code.
    /// </para>
    /// </summary>
    public sealed class SkillEffect
    {
        public EffectKind Kind { get; set; } = EffectKind.Damage;

        public TargetSelector Target { get; set; } = TargetSelector.RandomEnemy;

        /// <summary>How many targets a Random* selector picks. Ignored by All* and single selectors.</summary>
        public int MaxTargets { get; set; } = 1;

        /// <summary>Chance this effect lands on each target, in per-mille.</summary>
        public int ChancePerMille { get; set; } = 1000;

        // ---- Damage / Heal ----

        public DamageKind DamageKind { get; set; } = DamageKind.Physical;

        /// <summary>Stat the effect scales off. Defaults are derived from <see cref="DamageKind"/> when null.</summary>
        public Stat? ScalingStat { get; set; }

        /// <summary>Scaling in per-mille: 1000 means "100% of the scaling stat".</summary>
        public int PowerPerMille { get; set; } = 1000;

        /// <summary>Flat amount added after scaling. Mostly for fixed-value shields and heals.</summary>
        public int FlatAmount { get; set; }

        /// <summary>Number of separate hits. Each hit rolls crit and variance on its own.</summary>
        public int Hits { get; set; } = 1;

        public bool CanCrit { get; set; } = true;

        /// <summary>Share of damage dealt returned to the caster as healing, in per-mille.</summary>
        public int LifestealPerMille { get; set; }

        /// <summary>When true this effect picks its target even if an enemy is taunting.</summary>
        public bool IgnoresTaunt { get; set; }

        // ---- Status payload (StatModifier / DoT / HoT / Shield / Taunt / Stun / Revive) ----

        /// <summary>Identifies the status for stacking, cleansing and presentation. Required for status effects.</summary>
        public string StatusId { get; set; }

        /// <summary>Which stat a StatModifier changes.</summary>
        public Stat ModifiedStat { get; set; } = Stat.Attack;

        /// <summary>
        /// Magnitude in per-mille. Its meaning depends on the kind: stat change for
        /// StatModifier, share of the caster's scaling stat per tick for DoT/HoT, share of the
        /// caster's scaling stat for Shield, share of MaxHp restored for Revive.
        /// </summary>
        public int AmountPerMille { get; set; }

        public int DurationRounds { get; set; } = 2;

        public int MaxStacks { get; set; } = 1;

        public StackRule StackRule { get; set; } = StackRule.Refresh;

        /// <summary>How many statuses a Cleanse or Dispel removes. Use a large number for "all".</summary>
        public int RemoveCount { get; set; } = 1;

        // ---- Presentation hints. The simulator ignores these entirely. ----

        public string VfxId { get; set; }

        public string SfxId { get; set; }
    }

    /// <summary>
    /// A skill as authored in content. The same type is used for active turn actions,
    /// passives and leader skills; <see cref="Trigger"/> is what tells them apart.
    /// </summary>
    public sealed class SkillDefinition
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public TriggerKind Trigger { get; set; } = TriggerKind.OnAction;

        /// <summary>Chance the skill fires when its trigger occurs, in per-mille.</summary>
        public int ActivationChancePerMille { get; set; } = 1000;

        /// <summary>Rounds before the skill may fire again. 0 means every opportunity.</summary>
        public int CooldownRounds { get; set; }

        /// <summary>Cooldown the skill starts the battle on, for opener-limited ultimates.</summary>
        public int InitialCooldownRounds { get; set; }

        /// <summary>
        /// When true the skill only applies while its owner occupies the team's leader slot.
        /// </summary>
        public bool IsLeaderSkill { get; set; }

        public List<SkillEffect> Effects { get; set; } = new List<SkillEffect>();
    }
}
