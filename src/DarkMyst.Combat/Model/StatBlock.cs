using System;

namespace DarkMyst.Combat.Model
{
    /// <summary>
    /// A unit's numbers at the moment the battle starts. Everything is an integer: the
    /// simulator never touches floating point, so the same input always gives the same result
    /// on every platform.
    /// </summary>
    public struct StatBlock : IEquatable<StatBlock>
    {
        public int MaxHp;
        public int Attack;
        public int Magic;
        public int Defense;
        public int Resist;
        public int Speed;

        /// <summary>Chance to crit, in per-mille. Clamped to [0, 1000] when rolled.</summary>
        public int CritRate;

        /// <summary>Extra crit damage on top of the base crit multiplier, in per-mille.</summary>
        public int CritDamage;

        public int Get(Stat stat)
        {
            switch (stat)
            {
                case Stat.MaxHp: return MaxHp;
                case Stat.Attack: return Attack;
                case Stat.Magic: return Magic;
                case Stat.Defense: return Defense;
                case Stat.Resist: return Resist;
                case Stat.Speed: return Speed;
                case Stat.CritRate: return CritRate;
                case Stat.CritDamage: return CritDamage;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        public void Set(Stat stat, int value)
        {
            switch (stat)
            {
                case Stat.MaxHp: MaxHp = value; break;
                case Stat.Attack: Attack = value; break;
                case Stat.Magic: Magic = value; break;
                case Stat.Defense: Defense = value; break;
                case Stat.Resist: Resist = value; break;
                case Stat.Speed: Speed = value; break;
                case Stat.CritRate: CritRate = value; break;
                case Stat.CritDamage: CritDamage = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        /// <summary>Every stat this game defines, in a fixed order. Safe to iterate.</summary>
        public static readonly Stat[] AllStats =
        {
            Stat.MaxHp, Stat.Attack, Stat.Magic, Stat.Defense,
            Stat.Resist, Stat.Speed, Stat.CritRate, Stat.CritDamage
        };

        public bool Equals(StatBlock other)
        {
            return MaxHp == other.MaxHp
                && Attack == other.Attack
                && Magic == other.Magic
                && Defense == other.Defense
                && Resist == other.Resist
                && Speed == other.Speed
                && CritRate == other.CritRate
                && CritDamage == other.CritDamage;
        }

        public override bool Equals(object obj) => obj is StatBlock other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (Stat stat in AllStats)
                {
                    hash = (hash * 31) + Get(stat);
                }

                return hash;
            }
        }

        public override string ToString()
        {
            return "HP " + MaxHp + " ATK " + Attack + " MAG " + Magic + " DEF " + Defense
                 + " RES " + Resist + " SPD " + Speed + " CRT " + CritRate + " CRD " + CritDamage;
        }
    }
}
