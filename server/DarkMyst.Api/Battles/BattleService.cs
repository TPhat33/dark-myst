using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Battles
{
    /// <summary>
    /// The training-ground endpoint and the checksum-mismatch trap docs/07-testing-plan.md
    /// describes ("การกันผลเพี้ยนระหว่างไคลเอนต์กับเซิร์ฟเวอร์"). This class never computes a
    /// battle outcome itself — every number comes from <see cref="BattleSimulator.Run"/>, the same
    /// engine the client links against as a DLL (docs/01-architecture.md). Its only jobs are
    /// building a <see cref="TeamDefinition"/> from a player's owned characters (via
    /// <see cref="Progression.BuildUnit"/>, not a hand-rolled stat calculation) and comparing the
    /// result's checksum against whatever the client says it got.
    /// </summary>
    public sealed class BattleService
    {
        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _content;

        public BattleService(ApiDbContext db, ContentPackRegistry content)
        {
            _db = db;
            _content = content;
        }

        public async Task<RunBattleResponse> RunAsync(string accountId, RunBattleRequest request, CancellationToken ct)
        {
            ContentPack pack = _content.Latest;

            var instanceIds = request.Placements.Select(p => p.InstanceId).Distinct(StringComparer.Ordinal).ToList();
            List<OwnedCharacterEntity> characters = await _db.Characters.AsNoTracking()
                .Where(c => instanceIds.Contains(c.InstanceId))
                .ToListAsync(ct);

            var byId = characters.ToDictionary(c => c.InstanceId, c => c);
            var attacker = new TeamDefinition { TeamId = accountId, LeaderSlot = request.LeaderSlot };

            foreach (BattlePlacement placement in request.Placements)
            {
                if (!byId.TryGetValue(placement.InstanceId, out OwnedCharacterEntity character))
                {
                    throw new BattleRefusedException("Unknown character '" + placement.InstanceId + "'.");
                }

                if (character.OwnerId != accountId)
                {
                    throw new BattleRefusedException("Character '" + placement.InstanceId + "' does not belong to you.");
                }

                // The training ground is deliberately not affected by IsLocked/IsInUse — trying a
                // team you have already committed elsewhere is the entire point of a sandbox that
                // "ไม่จ่ายรางวัล" (docs/04-economy-spec.md).
                attacker.Units.Add(Progression.BuildUnit(pack, character.ToModel(), placement.Slot));
            }

            TeamDefinition defender;
            try
            {
                defender = Progression.BuildEncounterTeam(pack, request.EncounterId);
            }
            catch (ContentException ex)
            {
                throw new BattleRefusedException(ex.Message);
            }

            ulong seed = RandomSeed();
            BattleResult result = BattleSimulator.Run(new BattleRequest
            {
                Seed = seed,
                ContentVersion = pack.Version,
                Attacker = attacker,
                Defender = defender
            });

            bool? matched = null;
            if (!string.IsNullOrEmpty(request.ClientChecksum))
            {
                matched = string.Equals(request.ClientChecksum, result.Checksum, StringComparison.Ordinal);
                if (!matched.Value)
                {
                    _db.BattleChecksumMismatches.Add(new BattleChecksumMismatchEntity
                    {
                        AccountId = accountId,
                        EncounterId = request.EncounterId,
                        ServerChecksum = result.Checksum,
                        ClientChecksum = request.ClientChecksum,
                        RequestJson = JsonSerializer.Serialize(request),
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                }
            }

            return new RunBattleResponse(result, matched);
        }

        /// <summary>
        /// A training-ground fight has no expedition node or evolve request to derive a seed from,
        /// so it takes one from cryptographic randomness per call instead — determinism only needs
        /// to hold for a fixed <see cref="BattleRequest"/>, and the client always receives the seed
        /// this request actually used (it is echoed back on <see cref="BattleResult.Seed"/>) so a
        /// replay can still be verified byte-for-byte.
        /// </summary>
        private static ulong RandomSeed()
        {
            Span<byte> bytes = stackalloc byte[8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }
    }
}
