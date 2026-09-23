using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// One raw character-telemetry event (docs/08-metrics.md "เก็บเหตุการณ์ อย่าเก็บตัวเลขสรุป",
    /// docs/12-summon-spec.md ยังไม่ชื่อหัวข้อ "Telemetry ระดับตัวละคร"). Append-only: there is no
    /// update or delete code path anywhere in this API except the cascade that runs when the owning
    /// account itself is deleted (see <c>ApiDbContext.OnModelCreating</c>).
    /// <para>
    /// Written on the very same <see cref="ApiDbContext"/> the gameplay change that caused it uses,
    /// by <see cref="Telemetry.TelemetryWriter"/>, and never <c>SaveChanges</c>d separately — see
    /// that type's remarks for why an event exists if and only if the state change it describes
    /// actually committed.
    /// </para>
    /// </summary>
    public sealed class TelemetryEventEntity
    {
        public long Id { get; set; }

        /// <summary>The account this event is about. A player-account delete cascades onto this
        /// column (docs/08-metrics.md "ต้องลบได้จริงเมื่อผู้เล่นขอลบบัญชี").</summary>
        public string AccountId { get; set; }

        /// <summary>One of the event-type strings docs/10-backend-spec.md's telemetry section
        /// catalogues (<c>character_obtained</c>, <c>expedition_started</c>, ...). Not an enum: new
        /// event types are additive and should never need a migration to introduce.</summary>
        public string Type { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        /// <summary>Stamped from whichever <c>ContentPack</c> was actually in play for the state
        /// change this event describes — never "whatever is latest right now" — so a before/after
        /// balance comparison across a content publish is possible at all (docs/08-metrics.md
        /// principle 2).</summary>
        public string ContentVersion { get; set; }

        public string RulesVersion { get; set; }

        /// <summary>Raw JSON, mapped to a <c>jsonb</c> column. Shape is per-<see cref="Type"/>; see
        /// docs/10-backend-spec.md's event catalogue for each one's fields.</summary>
        public string PayloadJson { get; set; }
    }
}
