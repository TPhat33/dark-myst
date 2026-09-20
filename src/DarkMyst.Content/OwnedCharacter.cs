using System;

namespace DarkMyst.Content
{
    /// <summary>
    /// One character a player owns. This is the thing the game is really about, so it has an
    /// identity of its own, not just a row of numbers: the same character line obtained twice
    /// is two distinct objects with separate histories.
    /// <para>
    /// The server owns the authoritative copy. This type mirrors the columns the game rules
    /// actually read; presentation data (nickname, skin, favourite flag) lives alongside it in
    /// the database and is not needed to compute anything.
    /// </para>
    /// </summary>
    public sealed class OwnedCharacter
    {
        /// <summary>Stable unique id. Never reused, even after the character is consumed.</summary>
        public string InstanceId { get; set; }

        public string OwnerId { get; set; }

        /// <summary>Character id in the content pack, including the evolve stage.</summary>
        public string CharacterId { get; set; }

        public int Level { get; set; } = 1;

        public EvolveFocus Focus { get; set; } = EvolveFocus.None;

        /// <summary>
        /// Lifetime inherited bonus in per-mille, accumulated over every evolve and capped by
        /// <see cref="EvolveRulesData.InheritedBonusCapPerMille"/>.
        /// </summary>
        public int InheritedBonusPerMille { get; set; }

        /// <summary>
        /// Content version this character's numbers were last computed under. A rebalance
        /// publishes a new version; migration is an explicit, logged step, never a silent one.
        /// </summary>
        public string ContentVersion { get; set; }

        /// <summary>Player-set protection. Locked characters can never be spent as fodder.</summary>
        public bool IsLocked { get; set; }

        /// <summary>Set while the character sits in a saved team or an in-flight expedition.</summary>
        public bool IsInUse { get; set; }

        public DateTimeOffset ObtainedAt { get; set; }

        public OwnedCharacter Clone()
        {
            return new OwnedCharacter
            {
                InstanceId = InstanceId,
                OwnerId = OwnerId,
                CharacterId = CharacterId,
                Level = Level,
                Focus = Focus,
                InheritedBonusPerMille = InheritedBonusPerMille,
                ContentVersion = ContentVersion,
                IsLocked = IsLocked,
                IsInUse = IsInUse,
                ObtainedAt = ObtainedAt
            };
        }
    }
}
