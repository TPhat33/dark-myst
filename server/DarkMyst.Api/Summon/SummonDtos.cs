using System.Collections.Generic;

namespace DarkMyst.Api.Summon
{
    public sealed record SummonPullRequest(int Count);

    public sealed record SummonSparkRedeemRequest(string LineId);

    public sealed record SummonAttuneRequest(string InstanceId, int ShardsToSpend);

    /// <summary>One resolved pull, as returned to the client by <c>POST /summon/pull</c>.</summary>
    public sealed record SummonPullResultDto(
        string InstanceId, string CharacterId, string LineId, int Rarity, bool IsDuplicate, int ShardsGranted);

    public sealed record SummonPullResponse(
        List<SummonPullResultDto> Pulls,
        int PullsSinceLastPity,
        int PullsSinceLastFloor,
        int SparkPoints,
        bool CanRedeemSpark,
        int GemsSpent,
        int GemsRemaining);

    public sealed record SummonSparkRedeemResponse(
        string InstanceId, string CharacterId, string LineId, int Rarity, bool IsDuplicate, int ShardsGranted,
        int SparkPointsRemaining);

    public sealed record SummonAttuneResponse(
        string InstanceId, string LineId, int ShardsSpent, int ShardsBanked,
        int InheritedBonusPerMilleBefore, int InheritedBonusPerMilleAfter);

    public sealed record EchoShardBalanceDto(string LineId, int ShardCount);

    public sealed record SummonStateResponse(
        int PullsSinceLastPity, int PullsSinceLastFloor, int SparkPoints, int SparkThreshold, bool CanRedeemSpark,
        List<EchoShardBalanceDto> EchoShards);
}
