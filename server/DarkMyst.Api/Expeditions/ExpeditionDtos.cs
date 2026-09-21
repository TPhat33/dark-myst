using System.Collections.Generic;
using DarkMyst.Content;
using DarkMyst.Expedition.Model;

namespace DarkMyst.Api.Expeditions
{
    public sealed record ExpeditionPlacement(int Slot, string InstanceId);

    public sealed record StartExpeditionRequest(string StageId, int LeaderSlot, List<ExpeditionPlacement> Placements, ulong? Seed);

    public sealed record ChooseRequest(int ChoiceIndex);

    /// <summary>One team member as shown to the client — a thin projection of
    /// <see cref="RunTeamMemberState"/>, not a second copy of anything the rules compute.</summary>
    public sealed record RunTeamMemberDto(int Slot, string InstanceId, string CharacterId, int Level, int CurrentHp, int MaxHp);

    public sealed record AvailableChoiceDto(int ChoiceIndex, int NodeId, NodeKind Kind);

    /// <summary>
    /// What the client needs to keep playing or to redraw the screen after reconnecting —
    /// the "resume" half of docs/06-roadmap.md's acceptance criterion. Never includes the raw
    /// state JSON: that is a server implementation detail, not a wire contract.
    /// </summary>
    public sealed record ExpeditionRunSummary(
        string RunId,
        string StageId,
        string ContentVersion,
        RunStatus Status,
        string Lifecycle,
        int? CurrentNodeId,
        List<RunTeamMemberDto> Team,
        List<AvailableChoiceDto> AvailableChoices,
        int BankedGold,
        Dictionary<string, int> BankedMaterials,
        int BankedCharacterCount);

    /// <summary>Result of one <c>Choose</c> call: what happened at the node, plus the refreshed
    /// summary so the client never has to make a second request to know what it can do next.</summary>
    public sealed record ChooseResponse(ExpeditionNodeOutcome Outcome, ExpeditionRunSummary Summary);

    public sealed class ExpeditionRefusedException : System.Exception
    {
        public ExpeditionRefusedException(string message) : base(message)
        {
        }
    }
}
