using System;
using DarkMyst.Content;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// The persisted mirror of <see cref="OwnedCharacter"/> (docs/03-evolve-spec.md). Field for
    /// field the same columns the rules read; presentation-only data would live alongside this in
    /// a real deployment but there is none in this round.
    /// </summary>
    public sealed class OwnedCharacterEntity
    {
        public string InstanceId { get; set; }

        public string OwnerId { get; set; }

        public string CharacterId { get; set; }

        public int Level { get; set; }

        public EvolveFocus Focus { get; set; }

        public int InheritedBonusPerMille { get; set; }

        public string ContentVersion { get; set; }

        public bool IsLocked { get; set; }

        /// <summary>
        /// True while the character sits in a saved team or an active expedition run. Maintained
        /// directly by <see cref="Teams.TeamService"/> and <see cref="Expeditions.ExpeditionService"/>
        /// in the same transaction as the membership change that causes it, rather than computed
        /// live from a join — see docs/10-backend-spec.md for why a maintained flag was chosen over
        /// a recomputed one here.
        /// </summary>
        public bool IsInUse { get; set; }

        public DateTimeOffset ObtainedAt { get; set; }

        public OwnedCharacter ToModel()
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

        public static OwnedCharacterEntity FromModel(OwnedCharacter model)
        {
            return new OwnedCharacterEntity
            {
                InstanceId = model.InstanceId,
                OwnerId = model.OwnerId,
                CharacterId = model.CharacterId,
                Level = model.Level,
                Focus = model.Focus,
                InheritedBonusPerMille = model.InheritedBonusPerMille,
                ContentVersion = model.ContentVersion,
                IsLocked = model.IsLocked,
                IsInUse = model.IsInUse,
                ObtainedAt = model.ObtainedAt
            };
        }
    }
}
