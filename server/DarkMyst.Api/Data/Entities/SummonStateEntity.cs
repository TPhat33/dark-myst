using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// Per-account pity/floor/spark bookkeeping for the summon system (docs/12-summon-spec.md
    /// §"การันตีสามชั้น"). Only one banner exists right now, so this is keyed on
    /// <see cref="AccountId"/> alone — a future multi-banner column (<c>bannerId</c>) is a trivial
    /// add (widen the key to (AccountId, BannerId)) that this round deliberately does not build,
    /// per the task's scope.
    /// </summary>
    public sealed class SummonStateEntity
    {
        public string AccountId { get; set; }

        /// <summary>1-based-pull-count bookkeeping: pulls since the last
        /// <see cref="DarkMyst.Sim.SummonRules.PityTier"/>-or-better result. 0 means the last pull
        /// (or no pull yet) was already PityTier+.</summary>
        public int PullsSinceLastPity { get; set; }

        /// <summary>Same bookkeeping for <see cref="DarkMyst.Sim.SummonRules.FloorTier"/>.</summary>
        public int PullsSinceLastFloor { get; set; }

        /// <summary>Total pulls banked toward Spark (docs/12 "Spark สะสม 150 ครั้ง แลกสายไหนก็ได้")
        /// — incremented by 1 per pull, decremented by exactly
        /// <see cref="DarkMyst.Sim.SummonRules.SparkThreshold"/> on a successful
        /// <c>/summon/spark-redeem</c>; the leftover after a redemption carries over rather than
        /// resetting to 0.</summary>
        public int SparkPoints { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
