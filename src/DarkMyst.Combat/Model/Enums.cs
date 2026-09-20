namespace DarkMyst.Combat.Model
{
    /// <summary>Which side of the field a unit belongs to.</summary>
    public enum TeamSide
    {
        Attacker = 0,
        Defender = 1
    }

    /// <summary>
    /// Derived from the slot index, never authored directly. Slots 0-1 are the front line,
    /// slots 2-4 stand behind it.
    /// </summary>
    public enum Row
    {
        Front = 0,
        Back = 1
    }

    /// <summary>
    /// Ember beats Verdant beats Tide beats Ember. Radiant and Umbral are strong against
    /// each other. Neutral neither gains nor suffers.
    /// </summary>
    public enum Affinity
    {
        Neutral = 0,
        Ember = 1,
        Verdant = 2,
        Tide = 3,
        Radiant = 4,
        Umbral = 5
    }

    /// <summary>
    /// Crit rate and crit damage are stored in per-mille; every other stat is a plain integer.
    /// </summary>
    public enum Stat
    {
        MaxHp = 0,
        Attack = 1,
        Magic = 2,
        Defense = 3,
        Resist = 4,
        Speed = 5,
        CritRate = 6,
        CritDamage = 7
    }

    public enum DamageKind
    {
        /// <summary>Scales off Attack, mitigated by Defense, affected by the row modifier.</summary>
        Physical = 0,

        /// <summary>Scales off Magic, mitigated by Resist, ignores the row modifier.</summary>
        Magical = 1,

        /// <summary>Ignores mitigation, row and affinity. Used by damage-over-time ticks.</summary>
        True = 2
    }

    public enum EffectKind
    {
        Damage = 0,
        Heal = 1,
        StatModifier = 2,
        DamageOverTime = 3,
        HealOverTime = 4,
        Shield = 5,
        Cleanse = 6,
        Dispel = 7,
        Taunt = 8,
        Stun = 9,
        Revive = 10
    }

    public enum TargetSelector
    {
        Self = 0,
        RandomEnemy = 1,
        FrontRowEnemy = 2,
        LowestHpEnemy = 3,
        HighestAttackEnemy = 4,
        AllEnemies = 5,
        RandomAlly = 6,
        LowestHpAlly = 7,
        AllAllies = 8,

        /// <summary>The ally with the lowest slot index that is currently down. Revive only.</summary>
        LowestSlotDownedAlly = 9,

        /// <summary>Only meaningful inside a reaction: the unit that caused the trigger.</summary>
        TriggerSource = 10
    }

    public enum TriggerKind
    {
        OnBattleStart = 0,
        OnRoundStart = 1,

        /// <summary>Fires on the unit's own turn, before it picks an action.</summary>
        OnBeforeAction = 2,

        /// <summary>The unit's actual turn action. Every unit needs at least one of these.</summary>
        OnAction = 3,

        /// <summary>Fires on a unit that just took damage and survived.</summary>
        OnAfterDamaged = 4,

        /// <summary>Fires on every living ally when a team-mate goes down.</summary>
        OnAllyDown = 5,

        /// <summary>Fires on the unit itself as it goes down, before it is removed from play.</summary>
        OnDeath = 6
    }

    /// <summary>What happens when a status is applied to a unit that already has it.</summary>
    public enum StackRule
    {
        /// <summary>Keep one instance, reset its duration, keep the stronger magnitude.</summary>
        Refresh = 0,

        /// <summary>Keep one instance, add a stack up to MaxStacks, reset its duration.</summary>
        Stack = 1,

        /// <summary>Keep the existing instance untouched, drop the new application.</summary>
        Ignore = 2,

        /// <summary>Track the new application as its own instance with its own duration.</summary>
        Independent = 3
    }

    public enum BattleOutcome
    {
        AttackerVictory = 0,
        DefenderVictory = 1,
        Draw = 2
    }

    public enum BattleEndReason
    {
        DefenderWiped = 0,
        AttackerWiped = 1,

        /// <summary>Both sides still stand after the round limit; remaining HP share decides.</summary>
        RoundLimit = 2
    }
}
