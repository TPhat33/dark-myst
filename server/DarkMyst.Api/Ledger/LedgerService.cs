using System;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Ledger
{
    /// <summary>
    /// The one place any currency, material or character movement is applied. Every call writes
    /// both the maintained balance (<see cref="AccountEntity.Gold"/> /
    /// <see cref="InventoryMaterialEntity"/>) and an append-only <see cref="LedgerEntryEntity"/> row
    /// in the same unit of work, using the caller's already-open <see cref="ApiDbContext"/> (no
    /// transaction of its own — callers run inside <c>IdempotencyService.ExecuteAsync</c>'s
    /// transaction, see docs/10-backend-spec.md). This is what keeps docs/07-testing-plan.md's
    /// "ตรวจย้อนหลังได้" true by construction rather than by convention: there is no code path that
    /// changes a balance without also appending the entry that explains it.
    /// </summary>
    public sealed class LedgerService
    {
        private readonly ApiDbContext _db;

        public LedgerService(ApiDbContext db)
        {
            _db = db;
        }

        /// <summary>Applies a gold delta (positive or negative) to an already-tracked account and
        /// records why. Never lets gold go negative — the caller is expected to have validated
        /// affordability before spending; this is a last-line defense, not the primary check.</summary>
        public void ApplyGold(AccountEntity account, int delta, string reason, string idempotencyKey)
        {
            if (delta == 0)
            {
                return;
            }

            checked
            {
                account.Gold += delta;
            }

            if (account.Gold < 0)
            {
                throw new InvalidOperationException("Refusing to let account " + account.Id + " go negative on gold.");
            }

            _db.Ledger.Add(new LedgerEntryEntity
            {
                AccountId = account.Id,
                Kind = LedgerKind.Gold,
                RefId = null,
                Delta = delta,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        /// <summary>Applies a gems delta (positive or negative) — the same shape as
        /// <see cref="ApplyGold"/>, for the premium currency (see
        /// <see cref="AccountEntity.Gems"/>'s remarks). Never lets gems go negative; the caller is
        /// expected to have validated affordability first.</summary>
        public void ApplyGems(AccountEntity account, int delta, string reason, string idempotencyKey)
        {
            if (delta == 0)
            {
                return;
            }

            checked
            {
                account.Gems += delta;
            }

            if (account.Gems < 0)
            {
                throw new InvalidOperationException("Refusing to let account " + account.Id + " go negative on gems.");
            }

            _db.Ledger.Add(new LedgerEntryEntity
            {
                AccountId = account.Id,
                Kind = LedgerKind.Gems,
                RefId = null,
                Delta = delta,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        /// <summary>Applies a material delta. A missing <see cref="InventoryMaterialEntity"/> row
        /// is created on the first grant; a row is never deleted even if it reaches zero, so a
        /// listing shows "0 of an item you have handled before" the same way it would show any
        /// other quantity — no special case for empty.</summary>
        public async Task ApplyMaterialAsync(
            string accountId, string materialId, int delta, string reason, string idempotencyKey, CancellationToken ct)
        {
            if (delta == 0)
            {
                return;
            }

            InventoryMaterialEntity stack = await _db.Materials.FindAsync(new object[] { accountId, materialId }, ct);
            if (stack == null)
            {
                stack = new InventoryMaterialEntity { AccountId = accountId, MaterialId = materialId, Amount = 0 };
                _db.Materials.Add(stack);
            }

            checked
            {
                stack.Amount += delta;
            }

            if (stack.Amount < 0)
            {
                throw new InvalidOperationException(
                    "Refusing to let account " + accountId + "'s " + materialId + " stack go negative.");
            }

            _db.Ledger.Add(new LedgerEntryEntity
            {
                AccountId = accountId,
                Kind = LedgerKind.Material,
                RefId = materialId,
                Delta = delta,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        /// <summary>Records a character entering or leaving an account's collection (grant,
        /// evolve-consume). This does not touch <see cref="OwnedCharacterEntity"/> itself — the
        /// caller still owns adding/removing that row — it only appends the audit trail entry.</summary>
        public void RecordCharacterMovement(string accountId, string instanceId, int delta, string reason, string idempotencyKey)
        {
            _db.Ledger.Add(new LedgerEntryEntity
            {
                AccountId = accountId,
                Kind = LedgerKind.Character,
                RefId = instanceId,
                Delta = delta,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }
}
