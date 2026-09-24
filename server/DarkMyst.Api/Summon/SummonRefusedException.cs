using System;

namespace DarkMyst.Api.Summon
{
    /// <summary>Thrown for a clean, expected refusal of a summon/spark-redeem/attune request (bad
    /// input, insufficient gems/spark/shards, not-owned instance) — mapped to a 409 with a machine
    /// readable <see cref="Code"/>, never a 500. Same shape as <c>EvolveRefusedException</c>.</summary>
    public sealed class SummonRefusedException : Exception
    {
        public SummonRefusedException(string code, string message) : base(message)
        {
            Code = code;
        }

        /// <summary>Machine-readable reason, e.g. "not_enough_gems", "not_enough_spark",
        /// "insufficient_shards" — stable strings a client can branch on.</summary>
        public string Code { get; }
    }
}
