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
using DarkMyst.Api.Telemetry;
using DarkMyst.Combat;
using DarkMyst.Content;
using DarkMyst.Sim;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Summon
{
    /// <summary>
    /// Server-side implementation of docs/12-summon-spec.md's pull/spark/attune flows, one full
    /// operation per call, always inside the single transaction
    /// <see cref="Idempotency.IdempotencyService.ExecuteAsync"/> already opened (same shape as
    /// <c>EvolveService</c> — see that file's remarks). Rules math is never re-implemented here:
    /// every tier/pity/floor decision goes through <see cref="SummonEngine.ResolvePull"/>, the
    /// exact function <c>simrunner summon</c> measures, so the numbers this endpoint produces are
    /// provably the numbers docs/12 documents.
    /// <para>
    /// NOT LOCKED: <see cref="PullPriceGemsPerPull"/> is a placeholder currency cost. docs/04
    /// §"ตัวเลขที่ต้องมีก่อนล็อกเอกสารนี้" says the real price needs phase-C farm data before it can
    /// be locked; this constant exists only so the endpoint has *something* to deduct and is
    /// flagged here rather than presented as a real number.
    /// </para>
    /// </summary>
    public sealed class SummonService
    {
        /// <summary>NOT LOCKED — see this class's own remarks and docs/04-economy-spec.md
        /// §"ตัวเลขที่ต้องมีก่อนล็อกเอกสารนี้" item 4 ("สัดส่วนเวลาที่ลดลงถ้าจ่ายเงิน"). Chosen as a
        /// round placeholder only so <c>POST /summon/pull</c> has a real cost to deduct and test
        /// against; no bulk-pull discount is applied (a 10-pull costs exactly 10x a single pull)
        /// because inventing a discount percentage would be exactly the kind of unlocked number
        /// this comment exists to flag.</summary>
        public const int PullPriceGemsPerPull = 150;

        /// <summary>Only one banner exists (docs/12 "Banner ใบเดียวที่มีตัวชูโรงหมุนเวียน") — this
        /// is the constant every <c>summon_pulled</c> event's <c>bannerId</c> carries until a
        /// second banner is built.</summary>
        public const string DefaultBannerId = "default";

        private static readonly int[] AllowedPullCounts = { 1, 10 };

        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _content;
        private readonly LedgerService _ledger;
        private readonly TelemetryWriter _telemetry;

        public SummonService(ApiDbContext db, ContentPackRegistry content, LedgerService ledger, TelemetryWriter telemetry)
        {
            _db = db;
            _content = content;
            _ledger = ledger;
            _telemetry = telemetry;
        }

        public async Task<SummonPullResponse> PullAsync(string accountId, SummonPullRequest request, string idempotencyKey, CancellationToken ct)
        {
            if (request == null || !AllowedPullCounts.Contains(request.Count))
            {
                throw new SummonRefusedException("invalid_count", "count must be 1 or 10.");
            }

            AccountEntity account = await _db.Accounts.FirstAsync(a => a.Id == accountId, ct);
            int cost = checked(PullPriceGemsPerPull * request.Count);
            if (account.Gems < cost)
            {
                throw new SummonRefusedException(
                    "not_enough_gems", "Not enough gems: needs " + cost + ", has " + account.Gems + ".");
            }

            ContentPack pack = _content.Latest;
            Dictionary<int, List<CharacterData>> poolByTier = SummonEngine.BuildPool(pack);
            SummonRules rules = SummonRules.Proposed;

            SummonStateEntity state = await GetOrCreateStateAsync(accountId, ct);
            HashSet<string> ownedLines = await LoadOwnedLinesAsync(accountId, ct);
            Dictionary<string, EchoShardEntity> shardsByLine = await LoadShardsAsync(accountId, ct);

            _ledger.ApplyGems(account, -cost, "summon:pull", idempotencyKey);

            // Cryptographically-unpredictable seed per request (never derived from anything
            // guessable, and never the same across requests/accounts), still fed through
            // DeterministicRandom so there is exactly one RNG algorithm in the repo — see
            // DeterministicRandom's own remarks on why System.Random is never used here either.
            ulong seed = SummonTestHooks.SeedOverride?.Invoke() ?? GenerateSeed();
            var rng = new DeterministicRandom(seed);

            var results = new List<SummonPullResultDto>(request.Count);

            for (int pullIndex = 1; pullIndex <= request.Count; pullIndex++)
            {
                int pityBefore = state.PullsSinceLastPity;

                SummonPullOutcome outcome = SummonEngine.ResolvePull(
                    rules, poolByTier, state.PullsSinceLastPity, state.PullsSinceLastFloor, rng);

                state.PullsSinceLastPity = outcome.PullsSincePityAfter;
                state.PullsSinceLastFloor = outcome.PullsSinceFloorAfter;
                state.SparkPoints += 1;

                CharacterData picked = outcome.Character;
                bool isDuplicate = !ownedLines.Add(picked.LineId);
                int shardsGranted = 0;
                string grantedInstanceId = null;

                if (isDuplicate)
                {
                    shardsGranted = rules.DuplicateShardsForRarity(outcome.ActualTier);
                    EchoShardEntity shardRow = GetOrCreateShardRow(shardsByLine, accountId, picked.LineId);
                    shardRow.ShardCount += shardsGranted;
                }
                else
                {
                    var entity = new OwnedCharacterEntity
                    {
                        InstanceId = Guid.NewGuid().ToString("n"),
                        OwnerId = accountId,
                        CharacterId = picked.Id,
                        // docs/12 "สุ่มได้แค่สิทธิ์เข้าถึง" — every summoned character starts at
                        // stage-I's floor, level 1, no focus chosen yet and no inherited bonus.
                        // Matches DebugGrants.GrantCharacterAsync's convention for a fresh grant.
                        Level = 1,
                        Focus = EvolveFocus.None,
                        InheritedBonusPerMille = 0,
                        ContentVersion = pack.Version,
                        IsLocked = false,
                        IsInUse = false,
                        ObtainedAt = DateTimeOffset.UtcNow
                    };
                    _db.Characters.Add(entity);
                    _db.AccountLines.Add(new AccountLineEntity
                    {
                        AccountId = accountId,
                        LineId = picked.LineId,
                        FirstObtainedAt = DateTimeOffset.UtcNow
                    });
                    _ledger.RecordCharacterMovement(accountId, entity.InstanceId, +1, "summon:pull", idempotencyKey);
                    _telemetry.Add(accountId, TelemetryEventTypes.CharacterObtained, pack.Version, pack.Manifest.RulesVersion,
                        new CharacterObtainedPayload(
                            entity.InstanceId, picked.Id, picked.LineId, picked.Rarity, picked.EvolveStage,
                            CharacterObtainedSource.Summon, RunId: null, StageId: null));
                    grantedInstanceId = entity.InstanceId;
                }

                _telemetry.Add(accountId, TelemetryEventTypes.SummonPulled, pack.Version, pack.Manifest.RulesVersion,
                    new SummonPulledPayload(
                        DefaultBannerId, pullIndex, pityBefore, outcome.ActualTier, picked.Id, picked.LineId,
                        isDuplicate, shardsGranted));

                results.Add(new SummonPullResultDto(
                    grantedInstanceId, picked.Id, picked.LineId, outcome.ActualTier, isDuplicate, shardsGranted));
            }

            state.UpdatedAt = DateTimeOffset.UtcNow;

            return new SummonPullResponse(
                results,
                state.PullsSinceLastPity,
                state.PullsSinceLastFloor,
                state.SparkPoints,
                state.SparkPoints >= rules.SparkThreshold,
                cost,
                account.Gems);
        }

        public async Task<SummonSparkRedeemResponse> SparkRedeemAsync(
            string accountId, SummonSparkRedeemRequest request, string idempotencyKey, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.LineId))
            {
                throw new SummonRefusedException("invalid_line", "lineId is required.");
            }

            SummonRules rules = SummonRules.Proposed;
            SummonStateEntity state = await GetOrCreateStateAsync(accountId, ct);
            if (state.SparkPoints < rules.SparkThreshold)
            {
                throw new SummonRefusedException(
                    "not_enough_spark",
                    "Not enough Spark: needs " + rules.SparkThreshold + ", has " + state.SparkPoints + ".");
            }

            ContentPack pack = _content.Latest;
            CharacterData line = pack.Characters.FirstOrDefault(
                c => c.EvolveStage == 1 && c.LineId == request.LineId && c.Rarity >= 2 && c.Rarity <= 5);
            if (line == null)
            {
                throw new SummonRefusedException("unknown_line", "'" + request.LineId + "' is not a pullable stage-I line.");
            }

            HashSet<string> ownedLines = await LoadOwnedLinesAsync(accountId, ct);
            Dictionary<string, EchoShardEntity> shardsByLine = await LoadShardsAsync(accountId, ct);

            bool isDuplicate = !ownedLines.Add(line.LineId);
            int shardsGranted = 0;
            string grantedInstanceId = null;

            if (isDuplicate)
            {
                shardsGranted = rules.DuplicateShardsForRarity(line.Rarity);
                EchoShardEntity shardRow = GetOrCreateShardRow(shardsByLine, accountId, line.LineId);
                shardRow.ShardCount += shardsGranted;
            }
            else
            {
                var entity = new OwnedCharacterEntity
                {
                    InstanceId = Guid.NewGuid().ToString("n"),
                    OwnerId = accountId,
                    CharacterId = line.Id,
                    Level = 1,
                    Focus = EvolveFocus.None,
                    InheritedBonusPerMille = 0,
                    ContentVersion = pack.Version,
                    IsLocked = false,
                    IsInUse = false,
                    ObtainedAt = DateTimeOffset.UtcNow
                };
                _db.Characters.Add(entity);
                _db.AccountLines.Add(new AccountLineEntity
                {
                    AccountId = accountId,
                    LineId = line.LineId,
                    FirstObtainedAt = DateTimeOffset.UtcNow
                });
                _ledger.RecordCharacterMovement(accountId, entity.InstanceId, +1, "summon:spark-redeem", idempotencyKey);
                _telemetry.Add(accountId, TelemetryEventTypes.CharacterObtained, pack.Version, pack.Manifest.RulesVersion,
                    new CharacterObtainedPayload(
                        entity.InstanceId, line.Id, line.LineId, line.Rarity, line.EvolveStage,
                        CharacterObtainedSource.Spark, RunId: null, StageId: null));
                grantedInstanceId = entity.InstanceId;
            }

            // Consumes exactly the threshold — leftover carries over (docs/12 does not say Spark
            // resets to 0 on redemption, and resetting would throw away pulls the player already
            // made toward their *next* Spark).
            state.SparkPoints -= rules.SparkThreshold;
            state.UpdatedAt = DateTimeOffset.UtcNow;

            return new SummonSparkRedeemResponse(
                grantedInstanceId, line.Id, line.LineId, line.Rarity, isDuplicate, shardsGranted, state.SparkPoints);
        }

        public async Task<SummonAttuneResponse> AttuneAsync(
            string accountId, SummonAttuneRequest request, string idempotencyKey, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.InstanceId))
            {
                throw new SummonRefusedException("invalid_instance", "instanceId is required.");
            }

            if (request.ShardsToSpend <= 0)
            {
                throw new SummonRefusedException("invalid_amount", "shardsToSpend must be positive.");
            }

            List<OwnedCharacterEntity> locked = await _db.Characters
                .FromSqlInterpolated($"SELECT * FROM owned_characters WHERE instance_id = {request.InstanceId} FOR UPDATE")
                .ToListAsync(ct);
            OwnedCharacterEntity entity = locked.FirstOrDefault();
            if (entity == null)
            {
                throw new SummonRefusedException("unknown_character", "Unknown character '" + request.InstanceId + "'.");
            }

            if (entity.OwnerId != accountId)
            {
                throw new SummonRefusedException("not_owned", "That character does not belong to you.");
            }

            ContentPack pack = _content.Get(entity.ContentVersion);
            CharacterData character = pack.GetCharacter(entity.CharacterId);
            if (character.EvolveStage != 1)
            {
                throw new SummonRefusedException(
                    "not_stage_one", "Attune only applies to a stage-I character (Echo shards are per stage-I line).");
            }

            string lineId = character.LineId;
            EchoShardEntity shardRow = await _db.EchoShards.FindAsync(new object[] { accountId, lineId }, ct);
            int available = shardRow?.ShardCount ?? 0;
            if (request.ShardsToSpend > available)
            {
                throw new SummonRefusedException(
                    "insufficient_shards",
                    "Not enough " + lineId + " Echo shards: has " + available + ", asked to spend " + request.ShardsToSpend + ".");
            }

            int capPerMille = pack.Progression?.Evolve?.InheritedBonusCapPerMille ?? SummonRules.Proposed.DefaultAttuneCapPerMille;
            int before = entity.InheritedBonusPerMille;
            int roomPerMille = Math.Max(0, capPerMille - before);

            int requestedPerMille = request.ShardsToSpend / SummonRules.Proposed.ShardsPerPerMille;
            int gainedPerMille = Math.Min(requestedPerMille, roomPerMille);
            int shardsSpent = gainedPerMille * SummonRules.Proposed.ShardsPerPerMille;

            entity.InheritedBonusPerMille = before + gainedPerMille;
            if (shardRow != null)
            {
                shardRow.ShardCount -= shardsSpent;
            }

            _telemetry.Add(accountId, TelemetryEventTypes.AttuneCompleted, pack.Version, pack.Manifest.RulesVersion,
                new AttuneCompletedPayload(entity.InstanceId, lineId, shardsSpent, before, entity.InheritedBonusPerMille));

            int shardsBanked = request.ShardsToSpend - shardsSpent;
            return new SummonAttuneResponse(entity.InstanceId, lineId, shardsSpent, shardsBanked, before, entity.InheritedBonusPerMille);
        }

        public async Task<SummonStateResponse> GetStateAsync(string accountId, CancellationToken ct)
        {
            SummonStateEntity state = await _db.SummonStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.AccountId == accountId, ct);
            List<EchoShardEntity> shards = await _db.EchoShards.AsNoTracking()
                .Where(s => s.AccountId == accountId)
                .OrderBy(s => s.LineId)
                .ToListAsync(ct);

            int pullsSinceLastPity = state?.PullsSinceLastPity ?? 0;
            int pullsSinceLastFloor = state?.PullsSinceLastFloor ?? 0;
            int sparkPoints = state?.SparkPoints ?? 0;
            SummonRules rules = SummonRules.Proposed;

            return new SummonStateResponse(
                pullsSinceLastPity, pullsSinceLastFloor, sparkPoints, rules.SparkThreshold,
                sparkPoints >= rules.SparkThreshold,
                shards.Select(s => new EchoShardBalanceDto(s.LineId, s.ShardCount)).ToList());
        }

        private async Task<SummonStateEntity> GetOrCreateStateAsync(string accountId, CancellationToken ct)
        {
            List<SummonStateEntity> locked = await _db.SummonStates
                .FromSqlInterpolated($"SELECT * FROM summon_state WHERE account_id = {accountId} FOR UPDATE")
                .ToListAsync(ct);
            SummonStateEntity state = locked.FirstOrDefault();
            if (state != null)
            {
                return state;
            }

            state = new SummonStateEntity
            {
                AccountId = accountId,
                PullsSinceLastPity = 0,
                PullsSinceLastFloor = 0,
                SparkPoints = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.SummonStates.Add(state);
            // Flushed immediately so the row exists (and is locked, by virtue of being this
            // transaction's own uncommitted insert) for the rest of this same transaction to
            // mutate further below.
            await _db.SaveChangesAsync(ct);
            return state;
        }

        private async Task<HashSet<string>> LoadOwnedLinesAsync(string accountId, CancellationToken ct)
        {
            List<string> lines = await _db.AccountLines.AsNoTracking()
                .Where(l => l.AccountId == accountId)
                .Select(l => l.LineId)
                .ToListAsync(ct);
            return new HashSet<string>(lines, StringComparer.Ordinal);
        }

        private async Task<Dictionary<string, EchoShardEntity>> LoadShardsAsync(string accountId, CancellationToken ct)
        {
            List<EchoShardEntity> rows = await _db.EchoShards
                .Where(s => s.AccountId == accountId)
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.LineId, StringComparer.Ordinal);
        }

        private EchoShardEntity GetOrCreateShardRow(Dictionary<string, EchoShardEntity> shardsByLine, string accountId, string lineId)
        {
            if (shardsByLine.TryGetValue(lineId, out EchoShardEntity existing))
            {
                return existing;
            }

            var row = new EchoShardEntity { AccountId = accountId, LineId = lineId, ShardCount = 0 };
            _db.EchoShards.Add(row);
            shardsByLine[lineId] = row;
            return row;
        }

        private static ulong GenerateSeed()
        {
            Span<byte> bytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }
    }
}
