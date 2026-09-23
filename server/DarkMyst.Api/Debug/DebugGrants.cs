using System;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Api.Ledger;
using DarkMyst.Api.Telemetry;
using DarkMyst.Content;

namespace DarkMyst.Api.Debug
{
    /// <summary>
    /// Stands in for the shop/gacha/quest-reward systems docs/00-overview.md defers to phase E
    /// ("รายได้ ... ยังไม่เริ่ม") and docs/06-roadmap.md's phase E scope. Without one of those,
    /// there is no in-product way to get a character, gold or a material onto a fresh account —
    /// and phase D's acceptance criterion still needs an end-to-end demonstration (create a guest,
    /// run an expedition node, evolve a character) that does not depend on farming a real drop
    /// table for an unbounded number of tries. These three endpoints are that stand-in, gated to
    /// <c>Development</c> (or an explicit <c>Debug:AllowGrants</c> opt-in) in Program.cs so they
    /// never ship as a real feature — see docs/10-backend-spec.md "deliberately deferred".
    /// <para>
    /// Each method here is called from inside <c>IdempotencyService.ExecuteAsync</c> exactly like
    /// every other mutating endpoint (Program.cs) — the <paramref name="idempotencyKey"/> is
    /// threaded through to <see cref="LedgerService"/> purely for the audit trail (same as
    /// <c>EvolveService</c> does); the replay guarantee itself comes entirely from the caller's
    /// <c>ExecuteAsync</c> wrapper, not from anything in this class.
    /// </para>
    /// </summary>
    public static class DebugGrants
    {
        public static async Task<OwnedCharacterEntity> GrantCharacterAsync(
            ApiDbContext db, ContentPackRegistry content, LedgerService ledger, TelemetryWriter telemetry,
            string accountId, GrantCharacterRequest request, string idempotencyKey, CancellationToken ct)
        {
            ContentPack pack = content.Latest;
            CharacterData character = pack.GetCharacter(request.CharacterId); // throws ContentException if unknown

            EvolveFocus focus = string.IsNullOrEmpty(request.Focus)
                ? EvolveFocus.None
                : Enum.Parse<EvolveFocus>(request.Focus, ignoreCase: true);

            var entity = new OwnedCharacterEntity
            {
                InstanceId = Guid.NewGuid().ToString("n"),
                OwnerId = accountId,
                CharacterId = character.Id,
                Level = request.Level < 1 ? 1 : request.Level,
                Focus = focus,
                InheritedBonusPerMille = 0,
                ContentVersion = pack.Version,
                IsLocked = false,
                IsInUse = false,
                ObtainedAt = DateTimeOffset.UtcNow
            };

            db.Characters.Add(entity);
            ledger.RecordCharacterMovement(accountId, entity.InstanceId, +1, "debug:grant-character", idempotencyKey);
            telemetry.Add(accountId, TelemetryEventTypes.CharacterObtained, pack.Version, pack.Manifest.RulesVersion,
                new CharacterObtainedPayload(
                    entity.InstanceId, character.Id, character.LineId, character.Rarity, character.EvolveStage,
                    CharacterObtainedSource.Debug, RunId: null, StageId: null));
            await db.SaveChangesAsync(ct);
            return entity;
        }

        public static async Task GrantGoldAsync(
            ApiDbContext db, LedgerService ledger, string accountId, int amount, string idempotencyKey, CancellationToken ct)
        {
            AccountEntity account = await db.Accounts.FindAsync(new object[] { accountId }, ct);
            ledger.ApplyGold(account, amount, "debug:grant-gold", idempotencyKey);
            await db.SaveChangesAsync(ct);
        }

        public static Task GrantMaterialAsync(
            LedgerService ledger, string accountId, string materialId, int amount, string idempotencyKey, CancellationToken ct)
        {
            return ledger.ApplyMaterialAsync(accountId, materialId, amount, "debug:grant-material", idempotencyKey, ct);
        }
    }
}
