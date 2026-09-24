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

        /// <summary>Written once per pull by <c>POST /summon/pull</c> (docs/12-summon-spec.md
        /// "Telemetry ระดับตัวละคร", docs/10-backend-spec.md's summon section). Payload:
        /// <see cref="SummonPulledPayload"/>.</summary>
        public const string SummonPulled = "summon_pulled";

        /// <summary>Written by <c>POST /summon/attune</c> on every successful call (even one that
        /// spends 0 shards because the line was already at the cap — a call that did nothing
        /// useful is still a real thing that happened). Payload:
        /// <see cref="AttuneCompletedPayload"/>.</summary>
        public const string AttuneCompleted = "attune_completed";
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

    /// <summary>One resolved summon pull (docs/12-summon-spec.md "Telemetry ระดับตัวละคร",
    /// "ครั้งที่สุ่มก่อนได้ R4/R5 ตัวแรก"). <c>PityCounterBefore</c> is the pity counter's value
    /// going into this pull (0-based: how many non-PityTier+ pulls happened in a row before this
    /// one), matching what <c>SummonEngine.ResolvePull</c> is called with, not the value after.
    /// <c>BannerId</c> is always <c>"default"</c> in this round — see docs/12 "Banner ใบเดียว".</summary>
    public sealed record SummonPulledPayload(
        string BannerId, int PullIndex, int PityCounterBefore, int Tier, string CharacterId, string LineId,
        bool IsDuplicate, int ShardsGranted);

    /// <summary>One successful <c>POST /summon/attune</c> call (docs/12-summon-spec.md
    /// "ตัวซ้ำต้องไม่มีวันเป็นของเหลือ"). <c>ShardsSpent</c> is what was actually consumed toward
    /// the per-mille gain (always a multiple of <c>SummonRules.ShardsPerPerMille</c>), which can be
    /// less than the request's <c>shardsToSpend</c> — the remainder stays banked, see
    /// <c>Summon/SummonService.cs</c>.</summary>
    public sealed record AttuneCompletedPayload(
        string InstanceId, string LineId, int ShardsSpent, int InheritedBonusPerMilleBefore, int InheritedBonusPerMilleAfter);

    /// <summary>Source of a granted <see cref="Data.Entities.OwnedCharacterEntity"/>, as recorded on
    /// a <c>character_obtained</c> event. A plain string on the wire (docs/10-backend-spec.md), kept
    /// here only as the canonical set of values so every write site agrees on spelling.</summary>
    public static class CharacterObtainedSource
    {
        public const string Expedition = "expedition";
        public const string Debug = "debug";
        public const string Summon = "summon";
        public const string Spark = "spark";
    }
}
