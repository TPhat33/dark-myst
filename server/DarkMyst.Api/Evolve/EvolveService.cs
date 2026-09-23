using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Api.Ledger;
using DarkMyst.Api.Telemetry;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Evolve
{
    /// <summary>Thrown for a clean, expected refusal (a business rule blocked the evolve) — mapped
    /// to a 409 with the blocker list, never a 500.</summary>
    public sealed class EvolveRefusedException : Exception
    {
        public EvolveRefusedException(IReadOnlyList<string> blockers) : base(string.Join(" ", blockers))
        {
            Blockers = blockers;
        }

        public IReadOnlyList<string> Blockers { get; }
    }

    /// <summary>
    /// Implements the five mandatory steps of docs/03-evolve-spec.md
    /// "ลำดับที่เซิร์ฟเวอร์ต้องทำทุกครั้งที่ evolve", inside the one transaction
    /// <see cref="Idempotency.IdempotencyService.ExecuteAsync"/> already opened. Nothing here
    /// re-derives a stat formula or a cost table — every number comes from
    /// <see cref="Evolution.Preview"/>, called twice with exactly the same inputs the spec
    /// requires: once for <see cref="PreviewAsync"/> (read-only, shown before confirming) and once
    /// more here, against freshly locked rows, immediately before it is trusted to spend anything.
    /// </summary>
    public sealed class EvolveService
    {
        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _content;
        private readonly LedgerService _ledger;
        private readonly TelemetryWriter _telemetry;

        public EvolveService(ApiDbContext db, ContentPackRegistry content, LedgerService ledger, TelemetryWriter telemetry)
        {
            _db = db;
            _content = content;
            _ledger = ledger;
            _telemetry = telemetry;
        }

        public async Task<EvolvePreviewResponse> PreviewAsync(string accountId, EvolveRequest request, CancellationToken ct)
        {
            OwnedCharacterEntity subject = await _db.Characters.AsNoTracking()
                .FirstOrDefaultAsync(c => c.InstanceId == request.SubjectInstanceId, ct)
                ?? throw new EvolveRefusedException(new[] { "Unknown character '" + request.SubjectInstanceId + "'." });

            if (subject.OwnerId != accountId)
            {
                throw new EvolveRefusedException(new[] { "That character does not belong to you." });
            }

            List<OwnedCharacterEntity> fodderEntities = await _db.Characters.AsNoTracking()
                .Where(c => request.FodderInstanceIds.Contains(c.InstanceId))
                .ToListAsync(ct);

            EvolveFocus focus = ParseFocus(request.Focus);
            ContentPack pack = _content.Get(subject.ContentVersion);

            EvolvePreview preview = Evolution.Preview(
                pack, subject.ToModel(), ToModels(request.FodderInstanceIds, fodderEntities), focus);

            return ToDto(preview);
        }

        public async Task<IdempotentOperationResultOrRefusal> EvolveAsync(
            string accountId, EvolveRequest request, string idempotencyKey, CancellationToken ct)
        {
            // Lock the subject and every offered fodder character's row before touching anything.
            // Ordering by instance id gives every concurrent evolve the same lock-acquisition
            // order, so two evolves that both want (say) fodder A and B can never deadlock each
            // other by locking A-then-B while the other locks B-then-A.
            var allIds = new List<string> { request.SubjectInstanceId };
            allIds.AddRange(request.FodderInstanceIds);
            string[] orderedIds = allIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            List<OwnedCharacterEntity> locked = await _db.Characters
                .FromSqlInterpolated($"SELECT * FROM owned_characters WHERE instance_id = ANY({orderedIds}) ORDER BY instance_id FOR UPDATE")
                .ToListAsync(ct);

            var byId = locked.ToDictionary(c => c.InstanceId, c => c);

            if (!byId.TryGetValue(request.SubjectInstanceId, out OwnedCharacterEntity subject))
            {
                return Refused(new[] { "Unknown character '" + request.SubjectInstanceId + "'." });
            }

            if (subject.OwnerId != accountId)
            {
                return Refused(new[] { "That character does not belong to you." });
            }

            var fodderEntities = new List<OwnedCharacterEntity>();
            var missingFodder = new List<string>();
            foreach (string fodderId in request.FodderInstanceIds)
            {
                if (byId.TryGetValue(fodderId, out OwnedCharacterEntity entity))
                {
                    fodderEntities.Add(entity);
                }
                else
                {
                    missingFodder.Add(fodderId);
                }
            }

            if (missingFodder.Count > 0)
            {
                // Most commonly: someone else's concurrent evolve already consumed this exact
                // fodder and committed first, by the time this transaction got the lock. That is
                // "exactly one wins, the other fails cleanly" working as intended, not a bug.
                return Refused(missingFodder.Select(id => "Material '" + id + "' no longer exists.").ToList());
            }

            EvolveFocus focus = ParseFocus(request.Focus);
            ContentPack pack = _content.Get(subject.ContentVersion);

            // Re-run the exact same pure function against the rows we just locked. A client that
            // saw a valid preview a moment ago cannot ride that preview past a concurrent change —
            // a lock/team placement/level-up that landed in between shows up right here.
            EvolvePreview preview = Evolution.Preview(
                pack, subject.ToModel(), ToModels(request.FodderInstanceIds, fodderEntities), focus);

            var blockers = new List<string>(preview.Blockers);

            AccountEntity account = await _db.Accounts.FirstAsync(a => a.Id == accountId, ct);
            if (account.Gold < preview.GoldCost)
            {
                blockers.Add("Not enough gold: needs " + preview.GoldCost + ", has " + account.Gold + ".");
            }

            foreach (KeyValuePair<string, int> cost in preview.MaterialCost)
            {
                InventoryMaterialEntity stack = await _db.Materials.FindAsync(new object[] { accountId, cost.Key }, ct);
                int have = stack?.Amount ?? 0;
                if (have < cost.Value)
                {
                    blockers.Add("Not enough " + cost.Key + ": needs " + cost.Value + ", has " + have + ".");
                }
            }

            if (blockers.Count > 0)
            {
                return Refused(blockers);
            }

            // ---- Step 4: consume materials, gold and fodder, and write the new character — all
            // still inside the single transaction IdempotencyService opened. ----
            _ledger.ApplyGold(account, -preview.GoldCost, "evolve:gold-cost", idempotencyKey);
            foreach (KeyValuePair<string, int> cost in preview.MaterialCost)
            {
                await _ledger.ApplyMaterialAsync(accountId, cost.Key, -cost.Value, "evolve:material-cost", idempotencyKey, ct);
            }

            foreach (OwnedCharacterEntity fodder in fodderEntities)
            {
                _ledger.RecordCharacterMovement(accountId, fodder.InstanceId, -1, "evolve:consume-fodder", idempotencyKey);
                _db.Characters.Remove(fodder);
            }

            string fromCharacterId = subject.CharacterId;
            subject.CharacterId = preview.Result.CharacterId;
            subject.Level = preview.Result.Level;
            subject.Focus = preview.Result.Focus;
            subject.InheritedBonusPerMille = preview.Result.InheritedBonusPerMille;
            subject.ContentVersion = preview.Result.ContentVersion;

            // ---- Step 5: history. Fodder instance ids are recorded even though their rows are
            // gone the moment this transaction commits — see EvolveHistoryEntity's remarks. ----
            _db.EvolveHistory.Add(new EvolveHistoryEntity
            {
                OwnerId = accountId,
                SubjectInstanceId = subject.InstanceId,
                FromCharacterId = fromCharacterId,
                ResultCharacterId = subject.CharacterId,
                ResultInstanceId = subject.InstanceId,
                FodderInstanceIdsJson = JsonSerializer.Serialize(request.FodderInstanceIds),
                Focus = focus.ToString(),
                ContentVersion = subject.ContentVersion,
                GoldSpent = preview.GoldCost,
                MaterialsSpentJson = JsonSerializer.Serialize(preview.MaterialCost),
                GainedBonusPerMille = preview.GainedBonusPerMille,
                TotalBonusPerMille = preview.TotalBonusPerMille,
                CreatedAt = DateTimeOffset.UtcNow
            });

            CharacterData fromCharacter = pack.GetCharacter(fromCharacterId);
            CharacterData toCharacter = pack.GetCharacter(subject.CharacterId);
            int sameLineMaterialCount = fodderEntities.Count(f =>
                TelemetryContentResolver.Resolve(_content, f.ContentVersion, f.CharacterId).LineId == fromCharacter.LineId);

            // pack.Version, not subject.ContentVersion: every other event in this catalogue stamps
            // the ContentPack it actually resolved things against (docs/10-backend-spec.md), and
            // pack is that same pack here — subject.ContentVersion happens to equal it today (an
            // evolve's NextStageId always resolves within the pack it started under), but stamping
            // the pack directly keeps this event consistent by construction, not by coincidence.
            _telemetry.Add(accountId, TelemetryEventTypes.EvolveCompleted, pack.Version, pack.Manifest.RulesVersion,
                new EvolveCompletedPayload(
                    subject.InstanceId, fromCharacterId, subject.CharacterId, fromCharacter.LineId,
                    fromCharacter.EvolveStage, toCharacter.EvolveStage, subject.InheritedBonusPerMille,
                    request.FodderInstanceIds.ToList(), sameLineMaterialCount));

            var response = new EvolvedCharacterResponse(
                subject.InstanceId,
                subject.CharacterId,
                subject.Level,
                subject.Focus.ToString(),
                subject.InheritedBonusPerMille,
                subject.ContentVersion,
                ToStatDto(preview.StatsAfter),
                preview.GoldCost,
                preview.MaterialCost,
                request.FodderInstanceIds);

            return new IdempotentOperationResultOrRefusal(response);
        }

        private static List<OwnedCharacter> ToModels(IReadOnlyList<string> requestedIds, List<OwnedCharacterEntity> entities)
        {
            var byId = entities.ToDictionary(e => e.InstanceId, e => e);
            var result = new List<OwnedCharacter>();
            foreach (string id in requestedIds)
            {
                // Deliberately still passed through even when missing so Evolution.Preview's own
                // fodder-count check reports the real, honest count offered — the "unknown fodder"
                // condition is caught by the caller before/after this, not swallowed here.
                if (byId.TryGetValue(id, out OwnedCharacterEntity entity))
                {
                    result.Add(entity.ToModel());
                }
            }

            return result;
        }

        private static EvolveFocus ParseFocus(string focus)
        {
            if (string.IsNullOrEmpty(focus))
            {
                return EvolveFocus.None;
            }

            return Enum.TryParse(focus, ignoreCase: true, out EvolveFocus parsed)
                ? parsed
                : throw new EvolveRefusedException(new[] { "Unknown focus '" + focus + "'." });
        }

        private static EvolvePreviewResponse ToDto(EvolvePreview preview)
        {
            return new EvolvePreviewResponse(
                preview.CanEvolve,
                new List<string>(preview.Blockers),
                preview.ResultCharacterId,
                preview.GoldCost,
                new Dictionary<string, int>(preview.MaterialCost),
                preview.Essence,
                preview.GainedBonusPerMille,
                preview.TotalBonusPerMille,
                ToStatDto(preview.StatsBefore),
                // Zeroed when a structural blocker (final stage / no requirement for this stage)
                // made Evolution.Preview return before it ever computed a result stat block — the
                // blocker list already explains why, so this is not shown as an error on its own.
                ToStatDto(preview.StatsAfter));
        }

        private static StatBlockDto ToStatDto(StatBlock stats)
        {
            return new StatBlockDto(
                stats.MaxHp, stats.Attack, stats.Magic, stats.Defense, stats.Resist, stats.Speed, stats.CritRate, stats.CritDamage);
        }

        private static IdempotentOperationResultOrRefusal Refused(IReadOnlyList<string> blockers)
        {
            return new IdempotentOperationResultOrRefusal(blockers);
        }
    }

    /// <summary>Either the evolved character (success) or the list of reasons it was refused —
    /// both are a normal, storable outcome of the request, not an exception (see docs/10-backend-spec.md
    /// on why a business refusal still completes the idempotency record rather than failing it).</summary>
    public sealed class IdempotentOperationResultOrRefusal
    {
        public IdempotentOperationResultOrRefusal(EvolvedCharacterResponse success)
        {
            Success = success;
        }

        public IdempotentOperationResultOrRefusal(IReadOnlyList<string> blockers)
        {
            Blockers = blockers;
        }

        public EvolvedCharacterResponse Success { get; }

        public IReadOnlyList<string> Blockers { get; }

        public bool IsSuccess => Success != null;
    }
}
