using System;
using System.Collections.Generic;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>One named roster a player has saved. Placing a character here is one of the two
    /// things that sets <see cref="OwnedCharacterEntity.IsInUse"/> (docs/03-evolve-spec.md lists
    /// "in a team or an expedition" as a mandatory evolve-fodder refusal).</summary>
    public sealed class SavedTeamEntity
    {
        public string Id { get; set; }

        public string OwnerId { get; set; }

        public string Name { get; set; }

        public int LeaderSlot { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public List<SavedTeamMemberEntity> Members { get; set; } = new List<SavedTeamMemberEntity>();
    }

    /// <summary>One occupied slot in one saved team. Normalized (rather than a JSON blob on the
    /// team) so <see cref="Teams.TeamService"/> can ask "is this character in any other team" with
    /// a plain query when a team is edited or deleted.</summary>
    public sealed class SavedTeamMemberEntity
    {
        public string TeamId { get; set; }

        public int Slot { get; set; }

        public string InstanceId { get; set; }
    }
}
