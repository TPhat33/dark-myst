using System;

namespace DarkMyst.Sim
{
    /// <summary>
    /// Rates and thresholds for <c>simrunner summon</c> (docs/12-summon-spec.md, "โครงชั้นความหายาก"
    /// and "การันตีสามชั้น"). Every field is an integer — rolls are basis points out of 10000 (1bp =
    /// 0.01%), shard thresholds are integer counts — so the summon model stays consistent with the
    /// rest of the repo's "integers only" rule even though nothing here touches
    /// <c>DarkMyst.Combat</c>.
    /// <para>
    /// <see cref="Proposed"/> mirrors docs/12 exactly and is explicitly <b>not locked</b>: the
    /// doc's own banner says the mechanics are decided but the rate numbers are not, pending
    /// phase-C farm data ("สิ่งที่ต้องมีก่อนล็อกตัวเลข"). Anything printed from these values must
    /// say so — see <c>simrunner summon</c>'s report header.
    /// </para>
    /// </summary>
    public sealed class SummonRules
    {
        public int R5RateBasisPoints { get; }
        public int R4RateBasisPoints { get; }
        public int R3RateBasisPoints { get; }
        public int R2RateBasisPoints { get; }

        /// <summary>How many consecutive pulls without an R3+ result arm the floor: after this
        /// many misses in a row, the *next* pull is guaranteed R3+ (docs/12 "พื้นทุก 10 ครั้ง" —
        /// 9 misses arm it, the 10th pull is floored).</summary>
        public int FloorWindowPulls { get; }

        /// <summary>1-based pull count since the last R4+ (this pull included) at which soft pity
        /// starts adding basis points to the R4+ chance.</summary>
        public int SoftPityStartPull { get; }

        public int SoftPityBasisPointsPerStep { get; }

        /// <summary>1-based pull count since the last R4+ at which R4+ becomes guaranteed.</summary>
        public int HardPityPull { get; }

        public int SparkThreshold { get; }

        public int DuplicateShardsR2 { get; }
        public int DuplicateShardsR3 { get; }
        public int DuplicateShardsR4 { get; }
        public int DuplicateShardsR5 { get; }

        /// <summary>Echo shards needed for 1‰ of Attune ("4 shard = 1‰").</summary>
        public int ShardsPerPerMille { get; }

        /// <summary>
        /// Fallback Attune cap (per-mille) used only when no <c>ContentPack</c> is available (a
        /// rules-only unit test). <see cref="SummonSimulator"/> otherwise always prefers the
        /// pack's own <c>Progression.Evolve.InheritedBonusCapPerMille</c>, so the summon cap and
        /// the evolve cap it is standing in for can never silently drift apart.
        /// </summary>
        public int DefaultAttuneCapPerMille { get; }

        public SummonRules(
            int r5RateBasisPoints,
            int r4RateBasisPoints,
            int r3RateBasisPoints,
            int r2RateBasisPoints,
            int floorWindowPulls,
            int softPityStartPull,
            int softPityBasisPointsPerStep,
            int hardPityPull,
            int sparkThreshold,
            int duplicateShardsR2,
            int duplicateShardsR3,
            int duplicateShardsR4,
            int duplicateShardsR5,
            int shardsPerPerMille,
            int defaultAttuneCapPerMille)
        {
            if (r5RateBasisPoints + r4RateBasisPoints + r3RateBasisPoints + r2RateBasisPoints != 10000)
            {
                throw new ArgumentException("Base tier rates must sum to 10000 basis points (100%).");
            }

            if (floorWindowPulls < 1 || softPityStartPull < 1 || hardPityPull < 1 || sparkThreshold < 1
                || shardsPerPerMille < 1 || defaultAttuneCapPerMille < 1)
            {
                throw new ArgumentException("Every pity/spark/attune threshold must be positive.");
            }

            R5RateBasisPoints = r5RateBasisPoints;
            R4RateBasisPoints = r4RateBasisPoints;
            R3RateBasisPoints = r3RateBasisPoints;
            R2RateBasisPoints = r2RateBasisPoints;
            FloorWindowPulls = floorWindowPulls;
            SoftPityStartPull = softPityStartPull;
            SoftPityBasisPointsPerStep = softPityBasisPointsPerStep;
            HardPityPull = hardPityPull;
            SparkThreshold = sparkThreshold;
            DuplicateShardsR2 = duplicateShardsR2;
            DuplicateShardsR3 = duplicateShardsR3;
            DuplicateShardsR4 = duplicateShardsR4;
            DuplicateShardsR5 = duplicateShardsR5;
            ShardsPerPerMille = shardsPerPerMille;
            DefaultAttuneCapPerMille = defaultAttuneCapPerMille;
        }

        /// <summary>docs/12 §"โครงชั้นความหายาก", §"การันตีสามชั้น" and §"ตัวซ้ำต้องไม่มีวันเป็นของเหลือ",
        /// proposed but explicitly not locked — see the type's own doc comment.</summary>
        public static readonly SummonRules Proposed = new SummonRules(
            r5RateBasisPoints: 100,
            r4RateBasisPoints: 300,
            r3RateBasisPoints: 3600,
            r2RateBasisPoints: 6000,
            floorWindowPulls: 9,
            softPityStartPull: 40,
            softPityBasisPointsPerStep: 250,
            hardPityPull: 60,
            sparkThreshold: 150,
            duplicateShardsR2: 5,
            duplicateShardsR3: 10,
            duplicateShardsR4: 20,
            duplicateShardsR5: 40,
            shardsPerPerMille: 4,
            defaultAttuneCapPerMille: 300);

        /// <summary>Base chance of an R4+ result before any pity, in basis points (400 = 4%).</summary>
        public int R4PlusBaseBasisPoints => R5RateBasisPoints + R4RateBasisPoints;

        /// <summary>
        /// R4+ chance in basis points for a pull that is the <paramref name="pullsSinceLastR4Plus"/>-th
        /// pull since the last R4+ result (1-based, this pull included: a pull immediately after
        /// an R4+ is pull 1 of the next window). Flat at the base rate until
        /// <see cref="SoftPityStartPull"/>, then <see cref="SoftPityBasisPointsPerStep"/> more per
        /// further pull, and guaranteed (10000bp) from <see cref="HardPityPull"/> on.
        /// </summary>
        public int ComputeR4PlusChanceBasisPoints(int pullsSinceLastR4Plus)
        {
            if (pullsSinceLastR4Plus < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pullsSinceLastR4Plus), "Pull count must be 1-based and at least 1.");
            }

            if (pullsSinceLastR4Plus >= HardPityPull)
            {
                return 10000;
            }

            if (pullsSinceLastR4Plus < SoftPityStartPull)
            {
                return R4PlusBaseBasisPoints;
            }

            int steps = pullsSinceLastR4Plus - (SoftPityStartPull - 1);
            return R4PlusBaseBasisPoints + SoftPityBasisPointsPerStep * steps;
        }

        public int DuplicateShardsForRarity(int rarity)
        {
            switch (rarity)
            {
                case 2: return DuplicateShardsR2;
                case 3: return DuplicateShardsR3;
                case 4: return DuplicateShardsR4;
                case 5: return DuplicateShardsR5;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(rarity), "No duplicate shard rate is defined for rarity " + rarity + ".");
            }
        }

        /// <summary>
        /// This rule set with soft and hard pity pushed out past any pull count the tool will
        /// ever simulate, for a test that needs to measure the base R4+ rate in isolation. The
        /// 10-pull floor is left untouched: it only ever promotes a would-be R2 to R3 and has no
        /// bearing on the R4+ rate.
        /// </summary>
        public SummonRules WithPityDisabled()
        {
            return new SummonRules(
                R5RateBasisPoints, R4RateBasisPoints, R3RateBasisPoints, R2RateBasisPoints,
                FloorWindowPulls,
                softPityStartPull: int.MaxValue,
                SoftPityBasisPointsPerStep,
                hardPityPull: int.MaxValue,
                SparkThreshold,
                DuplicateShardsR2, DuplicateShardsR3, DuplicateShardsR4, DuplicateShardsR5,
                ShardsPerPerMille, DefaultAttuneCapPerMille);
        }
    }
}
