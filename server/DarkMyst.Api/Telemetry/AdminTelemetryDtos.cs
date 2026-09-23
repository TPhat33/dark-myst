using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DarkMyst.Api.Telemetry
{
    public sealed record TelemetryEventDto(
        long Id, string AccountId, string Type, DateTimeOffset OccurredAt,
        string ContentVersion, string RulesVersion, JsonElement Payload);

    public sealed record TelemetryEventsResponse(List<TelemetryEventDto> Items, long? NextAfter);

    public sealed record TelemetryStagePickRate(string StageId, int TotalStarts, int StartsWithLine, double PickRate);

    /// <summary>One side ("with the line" or "without it") of a win-rate comparison. The mean team
    /// level is reported alongside the win rate specifically so a reader can see whether a
    /// difference is the line itself or just the teams that happen to carry it being higher-level
    /// (docs/10-backend-spec.md's telemetry section, docs/12-summon-spec.md's popularity x strength
    /// framework). <c>Accounts</c> is the distinct-account count behind <c>N</c> battles — a few
    /// heavy players replaying the same fight can otherwise make a battle count look far more
    /// trustworthy than it is.</summary>
    public sealed record TelemetryWinRateGroup(int N, int Accounts, double? WinRate, double? MeanTeamLevel);

    public sealed record TelemetryWinRateComparison(string EncounterId, TelemetryWinRateGroup With, TelemetryWinRateGroup Without, bool LowSample);

    public sealed record TelemetryLineReport(
        string LineId,
        string DisplayName,
        int ObtainedCount,
        int DistinctOwnerCount,
        List<TelemetryStagePickRate> PickRatesByStage,
        List<TelemetryWinRateComparison> WinRatesByEncounter,
        TelemetryWinRateComparison WinRateOverall,
        int EvolveCompletedCount,
        int FirstEvolveCount,
        int ObtainedNeverUsedCount,
        double? MedianDaysObtainedToFirstUse);

    public sealed record TelemetryLinesResponse(List<string> ContentVersions, List<TelemetryLineReport> Lines);
}
