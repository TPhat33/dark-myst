using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Teams
{
    public sealed record TeamPlacement(int Slot, string InstanceId);

    public sealed record SaveTeamRequest(string Name, int LeaderSlot, List<TeamPlacement> Placements);

    public sealed record SavedTeamResponse(string Id, string Name, int LeaderSlot, List<TeamPlacement> Placements);

    public sealed class TeamRefusedException : Exception
    {
        public TeamRefusedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Saved rosters. The only rule this module enforces itself is the one docs/03-evolve-spec.md
    /// needs from it: a character sitting in a saved team is <see cref="OwnedCharacterEntity.IsInUse"/>,
    /// which evolve's fodder check reads directly (docs/10-backend-spec.md's transaction-boundary
    /// section explains why that flag is maintained here rather than computed at evolve time).
    /// </summary>
    public sealed class TeamService
    {
        private readonly ApiDbContext _db;

        public TeamService(ApiDbContext db)
        {
            _db = db;
        }

        public async Task<SavedTeamResponse> CreateAsync(string accountId, SaveTeamRequest request, CancellationToken ct)
        {
            var instanceIds = request.Placements.Select(p => p.InstanceId).Distinct(StringComparer.Ordinal).ToList();
            List<OwnedCharacterEntity> characters = await _db.Characters
                .Where(c => instanceIds.Contains(c.InstanceId))
                .ToListAsync(ct);

            var byId = characters.ToDictionary(c => c.InstanceId, c => c);
            foreach (string id in instanceIds)
            {
                if (!byId.TryGetValue(id, out OwnedCharacterEntity character))
                {
                    throw new TeamRefusedException("Unknown character '" + id + "'.");
                }

                if (character.OwnerId != accountId)
                {
                    throw new TeamRefusedException("Character '" + id + "' does not belong to you.");
                }

                if (character.IsInUse)
                {
                    throw new TeamRefusedException("Character '" + id + "' is already in a team or an expedition.");
                }
            }

            var team = new SavedTeamEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                OwnerId = accountId,
                Name = request.Name,
                LeaderSlot = request.LeaderSlot,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            foreach (TeamPlacement placement in request.Placements)
            {
                team.Members.Add(new SavedTeamMemberEntity { TeamId = team.Id, Slot = placement.Slot, InstanceId = placement.InstanceId });
                byId[placement.InstanceId].IsInUse = true;
            }

            _db.SavedTeams.Add(team);
            await _db.SaveChangesAsync(ct);

            return ToResponse(team);
        }

        public async Task DeleteAsync(string accountId, string teamId, CancellationToken ct)
        {
            SavedTeamEntity team = await _db.SavedTeams.Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == teamId, ct);

            if (team == null)
            {
                return;
            }

            if (team.OwnerId != accountId)
            {
                throw new TeamRefusedException("That team does not belong to you.");
            }

            var freedInstanceIds = team.Members.Select(m => m.InstanceId).ToList();
            _db.SavedTeams.Remove(team);
            await _db.SaveChangesAsync(ct);

            await ReleaseIfUnusedElsewhereAsync(freedInstanceIds, ct);
        }

        /// <summary>Clears <see cref="OwnedCharacterEntity.IsInUse"/> for characters no longer in
        /// any other saved team and not part of any active expedition run's team snapshot. Called
        /// after removing a team's membership (or an expedition ending) rather than kept as a
        /// live-computed value, so a normal read of a character's row needs no join.</summary>
        public async Task ReleaseIfUnusedElsewhereAsync(IReadOnlyList<string> instanceIds, CancellationToken ct)
        {
            if (instanceIds.Count == 0)
            {
                return;
            }

            var stillInATeam = await _db.SavedTeamMembers
                .Where(m => instanceIds.Contains(m.InstanceId))
                .Select(m => m.InstanceId)
                .Distinct()
                .ToListAsync(ct);

            var stillInATeamSet = new HashSet<string>(stillInATeam, StringComparer.Ordinal);

            List<OwnedCharacterEntity> characters = await _db.Characters
                .Where(c => instanceIds.Contains(c.InstanceId))
                .ToListAsync(ct);

            foreach (OwnedCharacterEntity character in characters)
            {
                if (!stillInATeamSet.Contains(character.InstanceId))
                {
                    character.IsInUse = false;
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        private static SavedTeamResponse ToResponse(SavedTeamEntity team)
        {
            return new SavedTeamResponse(
                team.Id,
                team.Name,
                team.LeaderSlot,
                team.Members.Select(m => new TeamPlacement(m.Slot, m.InstanceId)).ToList());
        }
    }
}
