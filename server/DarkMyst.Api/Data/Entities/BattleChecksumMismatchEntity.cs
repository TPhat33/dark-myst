using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// A recorded disagreement between a client's replayed checksum and the server's own
    /// recomputation, for a training-ground battle (docs/07-testing-plan.md
    /// "การกันผลเพี้ยนระหว่างไคลเอนต์กับเซิร์ฟเวอร์"). The whole request is kept so the mismatch
    /// can actually be investigated later, not just counted.
    /// </summary>
    public sealed class BattleChecksumMismatchEntity
    {
        public long Id { get; set; }

        public string AccountId { get; set; }

        public string EncounterId { get; set; }

        public string ServerChecksum { get; set; }

        public string ClientChecksum { get; set; }

        public string RequestJson { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
