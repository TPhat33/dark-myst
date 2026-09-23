using System.Collections.Generic;

namespace DarkMyst.Api.Telemetry
{
    /// <summary>The event-type strings this API ever writes. See docs/10-backend-spec.md's
    /// telemetry section for each one's payload shape and what design question it answers
    /// (docs/12-summon-spec.md "Telemetry ระดับตัวละคร").</summary>
    public static class TelemetryEventTypes
    {
        public const string CharacterObtained = "character_obtained";
        public const string ExpeditionStarted = "expedition_started";
        public const string BattleFinished = "battle_finished";
        public const string EvolveCompleted = "evolve_completed";
        public const string TeamSaved = "team_saved";

        /// <summary>Not written yet — no summon endpoint exists in this round. Kept here as the
        /// single place the intended shape is documented in code, next to docs/10-backend-spec.md's
        /// prose description, so whichever endpoint adds summoning finds it immediately.
        /// <para>
        /// Intended payload once a summon endpoint exists: <c>{ bannerId, pullIndex,
        /// pityCounterBefore, tier, characterId, lineId, isDuplicate, shardsGranted }</c>.
        /// </para></summary>
        public const string SummonPulled = "summon_pulled";
    }

    /// <summary>One team member as every event's <c>members</c> array reports it — the shape is
    /// identical across <c>expedition_started</c>, <c>battle_finished</c> and <c>team_saved</c> on
    /// purpose, so <c>/admin/telemetry/lines</c> reads one shape regardless of which event it came
    /// from.</summary>
    public sealed record TelemetryMemberSnapshot(
        string InstanceId, string CharacterId, string LineId, int EvolveStage, int Level, string Focus);

    public sealed record CharacterObtainedPayload(
        string InstanceId, string CharacterId, string LineId, int Rarity, int EvolveStage, string Source,
        string RunId, string StageId);

    public sealed record ExpeditionStartedPayload(
        string RunId, string StageId, List<TelemetryMemberSnapshot> Members);

    public sealed record BattleFinishedPayload(
        string Context, string RunId, string StageId, string EncounterId, string Outcome, int Turns,
        List<TelemetryMemberSnapshot> Members);

    public sealed record EvolveCompletedPayload(
        string InstanceId, string FromCharacterId, string ToCharacterId, string LineId, int FromStage,
        int ToStage, int InheritedBonusPerMilleAfter, List<string> MaterialInstanceIds, int SameLineMaterialCount);

    public sealed record TeamSavedPayload(string TeamId, List<TelemetryMemberSnapshot> Members);

    /// <summary>Source of a granted <see cref="Data.Entities.OwnedCharacterEntity"/>, as recorded on
    /// a <c>character_obtained</c> event. A plain string on the wire (docs/10-backend-spec.md), kept
    /// here only as the canonical set of values so every write site agrees on spelling; extensible
    /// for a future "summon" without a migration.</summary>
    public static class CharacterObtainedSource
    {
        public const string Expedition = "expedition";
        public const string Debug = "debug";
    }
}
