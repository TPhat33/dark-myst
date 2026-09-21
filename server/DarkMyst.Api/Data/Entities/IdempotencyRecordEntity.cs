using System;

namespace DarkMyst.Api.Data.Entities
{
    public enum IdempotencyStatus
    {
        /// <summary>Reserved, operation not finished yet. A concurrent replay must wait, not retry.</summary>
        InProgress = 0,

        Completed = 1,

        /// <summary>The operation ran and failed (a rejected evolve, a bad request, ...). The
        /// failure response is stored and replayed verbatim — the same key never gets a second
        /// attempt at the underlying operation. See docs/10-backend-spec.md.</summary>
        Failed = 2
    }

    /// <summary>
    /// The row that makes every mutating endpoint idempotent (docs/06-roadmap.md phase D:
    /// "เน็ตหลุดหรือคำขอซ้ำไม่ทำของหาย/เพิ่มซ้ำ"). Keyed on (AccountId, Endpoint, Key) so two
    /// different accounts — or the same account on two different endpoints — can reuse the same
    /// client-chosen key string without colliding.
    /// <para>
    /// This table is the shared mechanism: <see cref="Idempotency.IdempotencyService"/> is the
    /// only code that reads or writes it, and every mutating endpoint calls through it rather than
    /// re-implementing a check.
    /// </para>
    /// </summary>
    public sealed class IdempotencyRecordEntity
    {
        public string AccountId { get; set; }

        public string Endpoint { get; set; }

        public string Key { get; set; }

        /// <summary>Hash of the request body. A replay with the same key but a different body is a
        /// client bug, not a legitimate retry — flagged rather than silently served the stale
        /// response for a different request.</summary>
        public string RequestHash { get; set; }

        public IdempotencyStatus Status { get; set; }

        public int ResponseStatusCode { get; set; }

        /// <summary>JSON body of the stored response. Null while <see cref="Status"/> is InProgress.</summary>
        public string ResponseBody { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }
    }
}
