using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Content;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Telemetry
{
    /// <summary>
    /// The admin (docs/11-admin-spec.md-gated) read side of character telemetry
    /// (docs/10-backend-spec.md's telemetry section, docs/12-summon-spec.md "Telemetry ระดับตัวละคร").
    /// Every number <see cref="GetLineReportAsync"/> returns is computed here, on read, from the raw
    /// <c>telemetry_events</c> rows — nothing is stored pre-aggregated (docs/08-metrics.md principle
    /// 1, "เก็บเหตุการณ์ อย่าเก็บตัวเลขสรุป").
    /// </summary>
    public sealed class AdminTelemetryService
    {
        private const int DefaultEventsLimit = 500;
        private const int MaxEventsLimit = 5000;
        private const int LowSampleThreshold = 30;

        private static readonly string[] LineReportEventTypes =
        {
            TelemetryEventTypes.CharacterObtained,
            TelemetryEventTypes.ExpeditionStarted,
            TelemetryEventTypes.BattleFinished,
            TelemetryEventTypes.EvolveCompleted
        };

        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _content;
        private readonly JsonSerializerOptions _jsonOptions;

        public AdminTelemetryService(ApiDbContext db, ContentPackRegistry content, JsonSerializerOptions jsonOptions)
        {
            _db = db;
            _content = content;
            _jsonOptions = jsonOptions;
        }

        public async Task<TelemetryEventsResponse> ListEventsAsync(
            string type, DateTimeOffset? since, DateTimeOffset? until, int? limit, long? after, CancellationToken ct)
        {
            int take = limit.GetValueOrDefault(DefaultEventsLimit);
            take = Math.Clamp(take, 1, MaxEventsLimit);

            IQueryable<TelemetryEventEntity> query = _db.TelemetryEvents.AsNoTracking();

            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(e => e.Type == type);
            }

            if (since.HasValue)
            {
                query = query.Where(e => e.OccurredAt >= since.Value);
            }

            if (until.HasValue)
            {
                query = query.Where(e => e.OccurredAt <= until.Value);
            }

            if (after.HasValue)
            {
                query = query.Where(e => e.Id > after.Value);
            }

            // Keyset pagination on Id: an identity-always column, strictly increasing with insert
            // order, so "everything after the last id you saw" never skips or repeats a row even
            // if new events are written between pages — unlike offset paging, which both can do.
            List<TelemetryEventEntity> page = await query
                .OrderBy(e => e.Id)
                .Take(take + 1)
                .ToListAsync(ct);

            bool hasMore = page.Count > take;
            if (hasMore)
            {
                page.RemoveAt(page.Count - 1);
            }

            var items = page.Select(ToDto).ToList();
            long? nextAfter = hasMore && items.Count > 0 ? items[^1].Id : null;
            return new TelemetryEventsResponse(items, nextAfter);
        }

        public async Task<TelemetryLinesResponse> GetLineReportAsync(string contentVersion, CancellationToken ct)
        {
            string version = string.IsNullOrEmpty(contentVersion) ? _content.LatestVersion : contentVersion;
            ContentPack pack = _content.Get(version); // throws ContentVersionUnavailableException -> clean 409

            string[] relevantTypes = LineReportEventTypes;
            List<TelemetryEventEntity> events = await _db.TelemetryEvents.AsNoTracking()
                .Where(e => e.ContentVersion == version && relevantTypes.Contains(e.Type))
                .ToListAsync(ct);

            var obtained = new List<(string InstanceId, string LineId, string AccountId, DateTimeOffset ObtainedAt)>();
            var starts = new List<(string StageId, HashSet<string> LineIds)>();
            var battles = new List<(string EncounterId, bool Win, double MeanLevel, HashSet<string> LineIds)>();
            var evolves = new List<(string AccountId, DateTimeOffset OccurredAt, string LineId)>();

            // instanceId -> earliest time it showed up in an expedition_started or battle_finished
            // member list, across every stage/encounter — the "used" half of the obtained-but-
            // never-used metric (docs/12-summon-spec.md "ถูกดองหลังได้มา N วัน").
            var firstUseByInstance = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

            foreach (TelemetryEventEntity e in events)
            {
                switch (e.Type)
                {
                    case TelemetryEventTypes.CharacterObtained:
                    {
                        CharacterObtainedPayload payload = Deserialize<CharacterObtainedPayload>(e);
                        if (payload?.LineId != null)
                        {
                            obtained.Add((payload.InstanceId, payload.LineId, e.AccountId, e.OccurredAt));
                        }

                        break;
                    }

                    case TelemetryEventTypes.ExpeditionStarted:
                    {
                        ExpeditionStartedPayload payload = Deserialize<ExpeditionStartedPayload>(e);
                        if (payload == null)
                        {
                            break;
                        }

                        var lineIds = new HashSet<string>(
                            payload.Members.Where(m => m.LineId != null).Select(m => m.LineId), StringComparer.Ordinal);
                        starts.Add((payload.StageId, lineIds));
                        MarkFirstUse(firstUseByInstance, payload.Members, e.OccurredAt);
                        break;
                    }

                    case TelemetryEventTypes.BattleFinished:
                    {
                        BattleFinishedPayload payload = Deserialize<BattleFinishedPayload>(e);
                        if (payload == null || payload.Members.Count == 0)
                        {
                            break;
                        }

                        var lineIds = new HashSet<string>(
                            payload.Members.Where(m => m.LineId != null).Select(m => m.LineId), StringComparer.Ordinal);
                        double meanLevel = payload.Members.Average(m => (double)m.Level);
                        battles.Add((payload.EncounterId, payload.Outcome == "win", meanLevel, lineIds));
                        MarkFirstUse(firstUseByInstance, payload.Members, e.OccurredAt);
                        break;
                    }

                    case TelemetryEventTypes.EvolveCompleted:
                    {
                        EvolveCompletedPayload payload = Deserialize<EvolveCompletedPayload>(e);
                        if (payload?.LineId != null)
                        {
                            evolves.Add((e.AccountId, e.OccurredAt, payload.LineId));
                        }

                        break;
                    }
                }
            }

            // "First line an account ever finished an evolve on" — one entry per account, the
            // earliest evolve_completed row for it.
            var firstEvolveLineByAccount = evolves
                .GroupBy(x => x.AccountId, StringComparer.Ordinal)
                .Select(g => g.OrderBy(x => x.OccurredAt).First().LineId)
                .ToList();

            var startsByStage = starts.GroupBy(s => s.StageId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var lines = pack.Characters
                .Where(c => c.EvolveStage == 1 && !string.IsNullOrEmpty(c.LineId))
                .GroupBy(c => c.LineId, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.LineId, StringComparer.Ordinal)
                .ToList();

            var reports = new List<TelemetryLineReport>();
            foreach (CharacterData stageOne in lines)
            {
                string lineId = stageOne.LineId;

                var lineObtained = obtained.Where(o => o.LineId == lineId).ToList();
                int obtainedCount = lineObtained.Count;
                int distinctOwners = lineObtained.Select(o => o.AccountId).Distinct(StringComparer.Ordinal).Count();

                var pickRates = new List<TelemetryStagePickRate>();
                foreach (KeyValuePair<string, List<(string StageId, HashSet<string> LineIds)>> stage in startsByStage)
                {
                    int total = stage.Value.Count;
                    int withLine = stage.Value.Count(s => s.LineIds.Contains(lineId));
                    pickRates.Add(new TelemetryStagePickRate(
                        stage.Key, total, withLine, total > 0 ? (double)withLine / total : 0.0));
                }

                pickRates = pickRates.OrderBy(p => p.StageId, StringComparer.Ordinal).ToList();

                var winRatesByEncounter = battles.GroupBy(b => b.EncounterId, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => BuildWinRateComparison(g.Key, g.ToList(), lineId))
                    .ToList();

                TelemetryWinRateComparison overall = BuildWinRateComparison(null, battles, lineId);

                int evolveCompletedCount = evolves.Count(x => x.LineId == lineId);
                int firstEvolveCount = firstEvolveLineByAccount.Count(x => x == lineId);

                int neverUsed = 0;
                var daysToFirstUse = new List<double>();
                foreach ((string InstanceId, string LineId, string AccountId, DateTimeOffset ObtainedAt) o in lineObtained)
                {
                    if (firstUseByInstance.TryGetValue(o.InstanceId, out DateTimeOffset firstUse))
                    {
                        daysToFirstUse.Add((firstUse - o.ObtainedAt).TotalDays);
                    }
                    else
                    {
                        neverUsed++;
                    }
                }

                reports.Add(new TelemetryLineReport(
                    lineId,
                    stageOne.Name,
                    obtainedCount,
                    distinctOwners,
                    pickRates,
                    winRatesByEncounter,
                    overall,
                    evolveCompletedCount,
                    firstEvolveCount,
                    neverUsed,
                    Median(daysToFirstUse)));
            }

            return new TelemetryLinesResponse(new List<string> { version }, reports);
        }

        private static void MarkFirstUse(
            Dictionary<string, DateTimeOffset> firstUseByInstance, List<TelemetryMemberSnapshot> members, DateTimeOffset occurredAt)
        {
            foreach (TelemetryMemberSnapshot member in members)
            {
                if (member.InstanceId == null)
                {
                    continue;
                }

                if (!firstUseByInstance.TryGetValue(member.InstanceId, out DateTimeOffset existing) || occurredAt < existing)
                {
                    firstUseByInstance[member.InstanceId] = occurredAt;
                }
            }
        }

        private static TelemetryWinRateComparison BuildWinRateComparison(
            string encounterId, List<(string EncounterId, bool Win, double MeanLevel, HashSet<string> LineIds)> battles, string lineId)
        {
            var with = battles.Where(b => b.LineIds.Contains(lineId)).ToList();
            var without = battles.Where(b => !b.LineIds.Contains(lineId)).ToList();

            TelemetryWinRateGroup withGroup = ToGroup(with);
            TelemetryWinRateGroup withoutGroup = ToGroup(without);
            bool lowSample = with.Count < LowSampleThreshold || without.Count < LowSampleThreshold;

            return new TelemetryWinRateComparison(encounterId, withGroup, withoutGroup, lowSample);
        }

        private static TelemetryWinRateGroup ToGroup(List<(string EncounterId, bool Win, double MeanLevel, HashSet<string> LineIds)> group)
        {
            if (group.Count == 0)
            {
                return new TelemetryWinRateGroup(0, null, null);
            }

            double winRate = group.Count(b => b.Win) / (double)group.Count;
            double meanLevel = group.Average(b => b.MeanLevel);
            return new TelemetryWinRateGroup(group.Count, winRate, meanLevel);
        }

        private static double? Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            List<double> sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }

        private T Deserialize<T>(TelemetryEventEntity e) where T : class
        {
            if (string.IsNullOrEmpty(e.PayloadJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(e.PayloadJson, _jsonOptions);
        }

        private static TelemetryEventDto ToDto(TelemetryEventEntity e)
        {
            JsonElement payload = string.IsNullOrEmpty(e.PayloadJson)
                ? default
                : JsonSerializer.Deserialize<JsonElement>(e.PayloadJson);

            return new TelemetryEventDto(e.Id, e.AccountId, e.Type, e.OccurredAt, e.ContentVersion, e.RulesVersion, payload);
        }
    }
}
