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
    /// <para>
    /// The guarantee system has two independent knobs, generalized so this class can express both
    /// <see cref="Proposed"/>'s shape and a structurally different one (<see cref="GenshinLike"/>)
    /// with the same code path — see <c>simrunner summon --compare</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="PityTier"/> — the tier soft/hard pity guarantees "or
    /// better". <see cref="Proposed"/> pities on R4 (soft from pull 40, hard at 60).</description></item>
    /// <item><description><see cref="FloorTier"/> — the tier the every-N-pulls floor guarantees
    /// "or better". <see cref="Proposed"/> floors on R3 every 10 pulls. <c>FloorTier</c> must be
    /// strictly below <c>PityTier</c>: the floor only ever has to make good on pulls the pity
    /// roll did not already lift into <c>PityTier</c>+.</description></item>
    /// </list>
    /// </summary>
    public sealed class SummonRules
    {
        public int R5RateBasisPoints { get; }
        public int R4RateBasisPoints { get; }
        public int R3RateBasisPoints { get; }
        public int R2RateBasisPoints { get; }

        /// <summary>How many consecutive pulls without a <see cref="FloorTier"/>-or-better result
        /// arm the floor: after this many misses in a row, the *next* pull is guaranteed
        /// <see cref="FloorTier"/>-or-better (docs/12 "พื้นทุก 10 ครั้ง" for <see cref="Proposed"/>
        /// — 9 misses arm it, the 10th pull is floored).</summary>
        public int FloorWindowPulls { get; }

        /// <summary>1-based pull count since the last <see cref="PityTier"/>-or-better result
        /// (this pull included) at which soft pity starts adding basis points to the
        /// <see cref="PityTier"/>-or-better chance.</summary>
        public int SoftPityStartPull { get; }

        public int SoftPityBasisPointsPerStep { get; }

        /// <summary>1-based pull count since the last <see cref="PityTier"/>-or-better result at
        /// which <see cref="PityTier"/>-or-better becomes guaranteed.</summary>
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

        /// <summary>The tier (2-5) soft/hard pity guarantees "or better". See the type doc
        /// comment.</summary>
        public int PityTier { get; }

        /// <summary>The tier (2-5) the every-<see cref="FloorWindowPulls"/>-plus-1 floor
        /// guarantees "or better". Always strictly below <see cref="PityTier"/>. See the type doc
        /// comment.</summary>
        public int FloorTier { get; }

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
            int defaultAttuneCapPerMille,
            int pityTier = 4,
            int floorTier = 3)
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

            if (pityTier < 2 || pityTier > 5)
            {
                throw new ArgumentException("PityTier must be between 2 and 5.");
            }

            if (floorTier < 2 || floorTier >= pityTier)
            {
                throw new ArgumentException("FloorTier must be at least 2 and strictly below PityTier.");
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
            PityTier = pityTier;
            FloorTier = floorTier;
        }

        /// <summary>docs/12 §"โครงชั้นความหายาก", §"การันตีสามชั้น" and §"ตัวซ้ำต้องไม่มีวันเป็นของเหลือ",
        /// proposed but explicitly not locked — see the type's own doc comment. One pity track,
        /// pitying on R4 (<c>PityTier</c> 4); the 10-pull floor guarantees R3 (<c>FloorTier</c>
        /// 3).</summary>
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
            defaultAttuneCapPerMille: 300,
            pityTier: 4,
            floorTier: 3);

        /// <summary>
        /// Genshin Impact's public, stable wish structure, expressed in this model's shape, so it
        /// can be measured against <see cref="Proposed"/> with the same tool instead of argued
        /// about (docs/12 asks this comparison be measured, not argued):
        /// R5 0.6% base, soft pity from wish 74 rising +6.0pp/wish, hard pity at wish 90
        /// (<c>PityTier</c> 5). R4 5.1% base with a guaranteed 4★-or-better every 10 wishes
        /// (<c>FloorTier</c> 4, independent counter from the R5 pity). The remaining 94.3%
        /// (10000 - 60 - 510) splits R3:R2 = 36:60 by the same ratio <see cref="Proposed"/> uses
        /// for its own R3/R2 split — Genshin has no public 3★/2★-equivalent split to match, so
        /// this reuses ours rather than inventing one. The integer split rounds down for R3
        /// (9430 * 36 / 96 = 3536.25 → 3536) and gives the remainder to R2 (5894), so the four
        /// rates still sum to exactly 10000: 60 + 510 + 3536 + 5894 = 10000.
        /// <para>
        /// Genshin has no Spark-equivalent pity-free exchange; <see cref="SparkThreshold"/> is set
        /// to 180 here only so the report's "still missing at spark" column means something to
        /// compare against — 180 is the worst case to a guaranteed *featured* 5★ via the 50/50
        /// (two 90-pull hard pities, one of which may lose the 50/50 and roll off-banner).
        /// </para>
        /// </summary>
        public static readonly SummonRules GenshinLike = new SummonRules(
            r5RateBasisPoints: 60,
            r4RateBasisPoints: 510,
            r3RateBasisPoints: 3536,
            r2RateBasisPoints: 5894,
            floorWindowPulls: 9,
            softPityStartPull: 74,
            softPityBasisPointsPerStep: 600,
            hardPityPull: 90,
            sparkThreshold: 180,
            duplicateShardsR2: 5,
            duplicateShardsR3: 10,
            duplicateShardsR4: 20,
            duplicateShardsR5: 40,
            shardsPerPerMille: 4,
            defaultAttuneCapPerMille: 300,
            pityTier: 5,
            floorTier: 4);

        /// <summary>Base chance of a <see cref="PityTier"/>-or-better result before any pity, in
        /// basis points.</summary>
        public int PityBaseBasisPoints => SumRates(PityTier, 5);

        /// <summary>Base chance of an R4+ result before any pity, in basis points (400 = 4%). Kept
        /// as a literal R5+R4 quantity — independent of <see cref="PityTier"/> — because
        /// <see cref="ComputeR4PlusChanceBasisPoints"/> is a locked, tested surface that must keep
        /// meaning exactly "R4 or better", not "whatever PityTier currently is".</summary>
        public int R4PlusBaseBasisPoints => R5RateBasisPoints + R4RateBasisPoints;

        /// <summary>
        /// R4+ chance in basis points for a pull that is the <paramref name="pullsSinceLastR4Plus"/>-th
        /// pull since the last R4+ result (1-based, this pull included: a pull immediately after
        /// an R4+ is pull 1 of the next window). Flat at the base rate until
        /// <see cref="SoftPityStartPull"/>, then <see cref="SoftPityBasisPointsPerStep"/> more per
        /// further pull, and guaranteed (10000bp) from <see cref="HardPityPull"/> on.
        /// <para>
        /// Locked, tested surface (docs/12's R4-pity breakpoints) — kept literal and untouched by
        /// the <see cref="PityTier"/> generalization. <see cref="SummonSimulator"/> itself uses
        /// the generic <see cref="ComputePityChanceBasisPoints"/> below, which happens to compute
        /// the exact same numbers for <see cref="Proposed"/> (its PityTier is 4, so
        /// <see cref="PityBaseBasisPoints"/> == <see cref="R4PlusBaseBasisPoints"/>) but differs
        /// for a rule set like <see cref="GenshinLike"/> that pities on a different tier.
        /// </para>
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

        /// <summary>
        /// Generic form of <see cref="ComputeR4PlusChanceBasisPoints"/>: the chance, in basis
        /// points, of a <see cref="PityTier"/>-or-better result for a pull that is the
        /// <paramref name="pullsSincePity"/>-th pull since the last one (1-based, this pull
        /// included). Same ramp shape — flat at <see cref="PityBaseBasisPoints"/> until
        /// <see cref="SoftPityStartPull"/>, then <see cref="SoftPityBasisPointsPerStep"/> more per
        /// further pull, guaranteed from <see cref="HardPityPull"/> — but against whichever tier
        /// this rule set actually pities on. This is what <see cref="SummonSimulator"/> rolls
        /// against.
        /// </summary>
        public int ComputePityChanceBasisPoints(int pullsSincePity)
        {
            if (pullsSincePity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pullsSincePity), "Pull count must be 1-based and at least 1.");
            }

            if (pullsSincePity >= HardPityPull)
            {
                return 10000;
            }

            if (pullsSincePity < SoftPityStartPull)
            {
                return PityBaseBasisPoints;
            }

            int steps = pullsSincePity - (SoftPityStartPull - 1);
            return PityBaseBasisPoints + SoftPityBasisPointsPerStep * steps;
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

        /// <summary>Base rate, in basis points, for a single tier (2-5).</summary>
        public int RateBasisPoints(int tier)
        {
            switch (tier)
            {
                case 2: return R2RateBasisPoints;
                case 3: return R3RateBasisPoints;
                case 4: return R4RateBasisPoints;
                case 5: return R5RateBasisPoints;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), "Tier must be between 2 and 5.");
            }
        }

        /// <summary>Sum of base rates for tiers <paramref name="lowTierInclusive"/>..
        /// <paramref name="highTierInclusive"/> (both inclusive, low may exceed high — then the
        /// range is empty and this returns 0, which <see cref="SummonSimulator"/> relies on for a
        /// <c>FloorTier</c> that sits directly below <c>PityTier</c> with nothing between them).</summary>
        public int SumRates(int lowTierInclusive, int highTierInclusive)
        {
            int sum = 0;
            for (int tier = lowTierInclusive; tier <= highTierInclusive; tier++)
            {
                sum += RateBasisPoints(tier);
            }

            return sum;
        }

        /// <summary>
        /// This rule set with soft and hard pity pushed out past any pull count the tool will
        /// ever simulate, for a test that needs to measure the base <see cref="PityTier"/>-or-better
        /// rate in isolation. The floor is left untouched: it only ever promotes a would-be
        /// below-<see cref="FloorTier"/> result up to <see cref="FloorTier"/> and has no bearing on
        /// the pity rate.
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
                ShardsPerPerMille, DefaultAttuneCapPerMille,
                pityTier: PityTier,
                floorTier: FloorTier);
        }
    }
}
