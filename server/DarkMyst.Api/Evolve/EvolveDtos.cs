using System.Collections.Generic;

namespace DarkMyst.Api.Evolve
{
    public sealed record EvolveRequest(string SubjectInstanceId, List<string> FodderInstanceIds, string Focus);

    public sealed record EvolvePreviewResponse(
        bool CanEvolve,
        List<string> Blockers,
        string ResultCharacterId,
        int GoldCost,
        Dictionary<string, int> MaterialCost,
        int Essence,
        int GainedBonusPerMille,
        int TotalBonusPerMille,
        StatBlockDto StatsBefore,
        StatBlockDto StatsAfter);

    public sealed record StatBlockDto(
        int MaxHp, int Attack, int Magic, int Defense, int Resist, int Speed, int CritRate, int CritDamage);

    public sealed record EvolvedCharacterResponse(
        string InstanceId,
        string CharacterId,
        int Level,
        string Focus,
        int InheritedBonusPerMille,
        string ContentVersion,
        StatBlockDto StatsAfter,
        int GoldSpent,
        Dictionary<string, int> MaterialsSpent,
        List<string> ConsumedFodderInstanceIds);
}
