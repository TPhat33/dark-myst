using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Api.Ledger;
using DarkMyst.Content;
using DarkMyst.Expedition;
using DarkMyst.Expedition.Model;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Expeditions
{
    /// <summary>
    /// Server ownership of <see cref="DarkMyst.Expedition.ExpeditionRun"/>: everything
    /// docs/09-expedition-spec.md's "known limitations" section asks the calling layer to provide,
    /// because the library itself "ไม่รู้จักฐานข้อมูลหรือธุรกรรมของเซิร์ฟเวอร์เลย":
    /// <list type="bullet">
    /// <item>a run resolves into a <b>temporary clone</b> (<see cref="ExpeditionRun.Resume"/> always
    /// builds a fresh <see cref="ExpeditionRunState"/> from the stored JSON — nothing here mutates
    /// the row's current state directly) and the row is only overwritten once
    /// <see cref="ExpeditionRun.Choose"/> returns <i>without throwing</i>. If content resolution or
    /// the node resolve itself throws, the transaction that locked the row is rolled back and the
    /// row is exactly as it was before the call.</item>
    /// <item>the run row is locked with <c>FOR UPDATE</c> before cloning, so two concurrent
    /// <c>Choose</c> calls on the same run serialize at the database rather than racing to
    /// overwrite each other's state — the second waits, then resolves against the first's
    /// already-committed result.</item>
    /// <item>rewards accumulated in <see cref="ExpeditionRunState"/> are applied to the account's
    /// gold/materials/characters exactly once, at the moment the run stops being played (cleared,
    /// failed, or abandoned) — never per node. This keeps a mid-run crash from ever granting a
    /// treasure node's loot twice, and keeps `docs/09`'s "keepRewardsOnDefeat: false" behavior
    /// (the library already zeroes the in-memory banked totals in that case) correct without this
    /// layer having to duplicate that rule.</item>
    /// </list>
    /// </summary>
    public sealed class ExpeditionService
    {
        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _content;
        private readonly LedgerService _ledger;

        public ExpeditionService(ApiDbContext db, ContentPackRegistry content, LedgerService ledger)
        {
            _db = db;
            _content = content;
            _ledger = ledger;
        }

        public async Task<ExpeditionRunSummary> StartAsync(
            string accountId, StartExpeditionRequest request, CancellationToken ct)
        {
            ContentPack pack = _content.Latest;

            var instanceIds = request.Placements.Select(p => p.InstanceId).Distinct(StringComparer.Ordinal).ToList();

            // Same lock-then-validate shape as EvolveService: nobody else's concurrent request can
            // place (or evolve away) one of these characters between the check and the write.
            string[] orderedIds = instanceIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            List<OwnedCharacterEntity> locked = await _db.Characters
                .FromSqlInterpolated($"SELECT * FROM owned_characters WHERE instance_id = ANY({orderedIds}) ORDER BY instance_id FOR UPDATE")
                .ToListAsync(ct);

            var byId = locked.ToDictionary(c => c.InstanceId, c => c);
            var placements = new List<KeyValuePair<int, OwnedCharacter>>();

            foreach (ExpeditionPlacement placement in request.Placements)
            {
                if (!byId.TryGetValue(placement.InstanceId, out OwnedCharacterEntity character))
                {
                    throw new ExpeditionRefusedException("Unknown character '" + placement.InstanceId + "'.");
                }

                if (character.OwnerId != accountId)
                {
                    throw new ExpeditionRefusedException("Character '" + placement.InstanceId + "' does not belong to you.");
                }

                if (character.IsInUse)
                {
                    throw new ExpeditionRefusedException(
                        "Character '" + placement.InstanceId + "' is already in a team or an expedition.");
                }

                placements.Add(new KeyValuePair<int, OwnedCharacter>(placement.Slot, character.ToModel()));
            }

            ulong seed = request.Seed ?? RandomSeed();

            // ExpeditionRun.Start throws ExpeditionException for a bad slot/leader/stage id — let
            // that propagate as-is; the endpoint maps it to a clean refusal, same as any other
            // library validation error.
            ExpeditionRun run;
            try
            {
                run = ExpeditionRun.Start(pack, request.StageId, seed, request.LeaderSlot, placements);
            }
            catch (ExpeditionException ex)
            {
                throw new ExpeditionRefusedException(ex.Message);
            }

            var entity = new ExpeditionRunEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                OwnerId = accountId,
                StageId = request.StageId,
                ContentVersion = pack.Version,
                Status = run.State.Status.ToString(),
                Lifecycle = RunLifecycle.Active,
                StateJson = run.Serialize(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            foreach (string id in instanceIds)
            {
                byId[id].IsInUse = true;
            }

            _db.ExpeditionRuns.Add(entity);
            await _db.SaveChangesAsync(ct);

            return ToSummary(entity, run);
        }

        public async Task<ChooseResponse> ChooseAsync(
            string accountId, string runId, ChooseRequest request, CancellationToken ct)
        {
            ExpeditionRunEntity entity = await LockRunAsync(accountId, runId, ct);

            if (entity.Lifecycle != RunLifecycle.Active)
            {
                throw new ExpeditionRefusedException("Run '" + runId + "' was abandoned and no longer accepts choices.");
            }

            // docs/05-content-pipeline.md / docs/09-expedition-spec.md: an in-flight run must
            // finish under the version it began with. If that version has since been retired from
            // this server, ContentVersionUnavailableException propagates as-is (ApiErrors maps it
            // to a specific 409 "content_version_unavailable", not a generic expedition refusal) —
            // the honest answer is refusal, never a silent fallback to latest.
            ContentPack pack = _content.Get(entity.ContentVersion);

            ExpeditionRun clone = ExpeditionRun.Resume(pack, entity.StateJson);

            ExpeditionNodeOutcome outcome;
            try
            {
                outcome = clone.Choose(pack, request.ChoiceIndex);
            }
            catch (ExpeditionException ex)
            {
                // The clone was never persisted — entity.StateJson (and the row) are untouched.
                throw new ExpeditionRefusedException(ex.Message);
            }

            if (outcome.RunEnded)
            {
                await SettleAsync(accountId, clone.State, "expedition:run-cleared-or-failed", ct);
            }

            entity.Status = clone.State.Status.ToString();
            entity.StateJson = clone.Serialize();
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new ChooseResponse(outcome, ToSummary(entity, clone));
        }

        public async Task<ExpeditionRunSummary> AbandonAsync(string accountId, string runId, CancellationToken ct)
        {
            ExpeditionRunEntity entity = await LockRunAsync(accountId, runId, ct);

            if (entity.Lifecycle != RunLifecycle.Active)
            {
                throw new ExpeditionRefusedException("Run '" + runId + "' was already abandoned.");
            }

            ContentPack pack = _content.Get(entity.ContentVersion);

            ExpeditionRun run = ExpeditionRun.Resume(pack, entity.StateJson);

            if (run.State.Status != RunStatus.InProgress)
            {
                // Already Cleared or Failed via a prior Choose call, which already settled its own
                // rewards (ChooseAsync). Abandoning it now would be a no-op at best and a
                // mislabeled Lifecycle at worst — refuse instead of quietly overwriting how it
                // actually ended.
                throw new ExpeditionRefusedException("Run '" + runId + "' has already ended (" + run.State.Status + ").");
            }

            // Design decision (docs do not dictate this): abandoning is treated as walking away
            // with whatever has been banked so far, the same way finishing the stage would be —
            // not as forfeiting it. Only an actual battle loss can zero the banked totals
            // (StageData.KeepRewardsOnDefeat, applied by the library itself), never quitting.
            await SettleAsync(accountId, run.State, "expedition:abandoned", ct);

            entity.Lifecycle = RunLifecycle.Abandoned;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return ToSummary(entity, run);
        }

        public async Task<ExpeditionRunSummary> GetAsync(string accountId, string runId, CancellationToken ct)
        {
            ExpeditionRunEntity entity = await _db.ExpeditionRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, ct)
                ?? throw new ExpeditionRefusedException("Unknown run '" + runId + "'.");

            if (entity.OwnerId != accountId)
            {
                throw new ExpeditionRefusedException("That run does not belong to you.");
            }

            ContentPack pack = _content.Get(entity.ContentVersion);

            ExpeditionRun run = ExpeditionRun.Resume(pack, entity.StateJson);
            return ToSummary(entity, run);
        }

        /// <summary>Grants everything banked in <paramref name="state"/> to the account and
        /// releases every placed character's in-use flag (unless a saved team still holds it) —
        /// the one place a run's rewards ever reach a player's persistent inventory.</summary>
        private async Task SettleAsync(string accountId, ExpeditionRunState state, string reason, CancellationToken ct)
        {
            AccountEntity account = await _db.Accounts.FirstAsync(a => a.Id == accountId, ct);
            _ledger.ApplyGold(account, state.BankedGold, reason, idempotencyKey: null);

            foreach (MaterialStack stack in state.BankedMaterials)
            {
                await _ledger.ApplyMaterialAsync(accountId, stack.MaterialId, stack.Amount, reason, idempotencyKey: null, ct);
            }

            foreach (string characterId in state.BankedCharacterIds)
            {
                var granted = new OwnedCharacterEntity
                {
                    InstanceId = Guid.NewGuid().ToString("n"),
                    OwnerId = accountId,
                    CharacterId = characterId,
                    Level = 1,
                    Focus = EvolveFocus.None,
                    InheritedBonusPerMille = 0,
                    ContentVersion = state.ContentVersion,
                    IsLocked = false,
                    IsInUse = false,
                    ObtainedAt = DateTimeOffset.UtcNow
                };
                _db.Characters.Add(granted);
                _ledger.RecordCharacterMovement(accountId, granted.InstanceId, +1, reason, idempotencyKey: null);
            }

            var placedInstanceIds = state.Team.Select(m => m.InstanceId).ToList();
            List<OwnedCharacterEntity> placed = await _db.Characters
                .Where(c => placedInstanceIds.Contains(c.InstanceId))
                .ToListAsync(ct);

            var stillInATeam = new HashSet<string>(
                await _db.SavedTeamMembers.Where(m => placedInstanceIds.Contains(m.InstanceId))
                    .Select(m => m.InstanceId).ToListAsync(ct),
                StringComparer.Ordinal);

            foreach (OwnedCharacterEntity character in placed)
            {
                if (!stillInATeam.Contains(character.InstanceId))
                {
                    character.IsInUse = false;
                }
            }
        }

        /// <summary>Locks the run row with <c>FOR UPDATE</c> so a concurrent choose/abandon on the
        /// same run serializes instead of racing — see the type-level remarks.</summary>
        private async Task<ExpeditionRunEntity> LockRunAsync(string accountId, string runId, CancellationToken ct)
        {
            // xmin (ExpeditionRunEntity.Version, the optimistic-concurrency token) is a Postgres
            // system column: "SELECT *" does not include it, so raw SQL must name it explicitly or
            // EF's materializer fails looking for a column that silently was not selected.
            ExpeditionRunEntity entity = await _db.ExpeditionRuns
                .FromSqlInterpolated($"SELECT *, xmin FROM expedition_runs WHERE id = {runId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new ExpeditionRefusedException("Unknown run '" + runId + "'.");

            if (entity.OwnerId != accountId)
            {
                throw new ExpeditionRefusedException("That run does not belong to you.");
            }

            return entity;
        }

        private static ulong RandomSeed()
        {
            Span<byte> bytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }

        private static ExpeditionRunSummary ToSummary(ExpeditionRunEntity entity, ExpeditionRun run)
        {
            var team = run.State.Team
                .Select(m => new RunTeamMemberDto(m.Slot, m.InstanceId, m.CharacterId, m.Level, m.CurrentHp, m.MaxHp))
                .ToList();

            IReadOnlyList<ExpeditionChoice> choices = run.AvailableChoices();
            var choiceDtos = new List<AvailableChoiceDto>();
            for (int i = 0; i < choices.Count; i++)
            {
                choiceDtos.Add(new AvailableChoiceDto(i, choices[i].NodeId, choices[i].Kind));
            }

            var materials = new Dictionary<string, int>();
            foreach (MaterialStack stack in run.State.BankedMaterials)
            {
                materials[stack.MaterialId] = stack.Amount;
            }

            return new ExpeditionRunSummary(
                entity.Id,
                entity.StageId,
                entity.ContentVersion,
                run.State.Status,
                entity.Lifecycle.ToString(),
                run.State.CurrentNodeId,
                team,
                choiceDtos,
                run.State.BankedGold,
                materials,
                run.State.BankedCharacterIds.Count);
        }
    }
}
