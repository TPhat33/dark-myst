using System;
using System.Collections.Generic;
using DarkMyst.Combat;
using DarkMyst.Content;

namespace DarkMyst.Sim
{
    /// <summary>
    /// One resolved pull: the actual pullable line the roll landed on (after folding any empty
    /// tier down, docs/12-summon-spec.md "โครงชั้นความหายาก"), and the pity/floor counters as they
    /// stand immediately after this pull (docs/12 "การันตีสามชั้น").
    /// </summary>
    public readonly struct SummonPullOutcome
    {
        public SummonPullOutcome(CharacterData character, int actualTier, int pullsSincePityAfter, int pullsSinceFloorAfter)
        {
            Character = character;
            ActualTier = actualTier;
            PullsSincePityAfter = pullsSincePityAfter;
            PullsSinceFloorAfter = pullsSinceFloorAfter;
        }

        public CharacterData Character { get; }

        public int ActualTier { get; }

        public int PullsSincePityAfter { get; }

        public int PullsSinceFloorAfter { get; }
    }

    /// <summary>
    /// The single, pure implementation of "resolve one pull's outcome" for
    /// <see cref="SummonRules"/> (docs/12-summon-spec.md §"การันตีสามชั้น", §"โครงชั้นความหายาก").
    /// Extracted out of <see cref="SummonSimulator"/> (which still calls this, unchanged in
    /// behavior — see that type's own remarks and the byte-identical-output lock test in
    /// <c>tests/DarkMyst.Sim.Tests</c>) so a per-account, per-request server resolver
    /// (<c>server/DarkMyst.Api/Summon/SummonEngineService.cs</c>) can call the exact same rules
    /// code the batch simulator measures, rather than a second, hand-copied implementation that
    /// could silently drift from it.
    /// <para>
    /// Deliberately stateless and free of any notion of "many players" or "a report" — it knows
    /// about exactly one pull, given the caller's own pity/floor counters and RNG. Everything
    /// about *who* is pulling, *how many* pulls, or *what to do with the result* is the caller's
    /// job (the simulator's per-player loop, or the server's per-request transaction).
    /// </para>
    /// </summary>
    public static class SummonEngine
    {
        /// <summary>
        /// Resolves exactly one pull: rolls pity first (soft/hard, docs/12), then the floor, then
        /// picks an actual character within the resolved tier (folding down through empty tiers).
        /// Consumes RNG words in exactly the same order <see cref="SummonSimulator"/> always has,
        /// so seeding this with the same seed/stream as before produces the identical stream.
        /// </summary>
        /// <param name="rules">The rule set to resolve against (<see cref="SummonRules.Proposed"/>
        /// for the real game).</param>
        /// <param name="poolByTier">Pullable stage-I lines keyed by rarity tier (2-5) — see
        /// <see cref="BuildPool"/> for how to build this from a <see cref="ContentPack"/>.</param>
        /// <param name="pullsSincePity">1-based-pull-count bookkeeping: how many pulls have
        /// happened since the last <see cref="SummonRules.PityTier"/>-or-better result, *before*
        /// this pull (0 if the last pull itself was PityTier+, or this is the player's first
        /// pull).</param>
        /// <param name="pullsSinceFloor">Same bookkeeping for <see cref="SummonRules.FloorTier"/>.</param>
        /// <param name="rng">The caller's RNG stream — see <see cref="DeterministicRandom"/>'s own
        /// docs for why this must never be <see cref="System.Random"/>.</param>
        public static SummonPullOutcome ResolvePull(
            SummonRules rules,
            IReadOnlyDictionary<int, List<CharacterData>> poolByTier,
            int pullsSincePity,
            int pullsSinceFloor,
            DeterministicRandom rng)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (poolByTier == null)
            {
                throw new ArgumentNullException(nameof(poolByTier));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            // Pity check first (docs/12 §"การันตีสามชั้น"): roll for PityTier-or-better at this
            // pull's (possibly soft/hard-boosted) chance. If it fires, pick among the tiers
            // PityTier..5 by base-rate proportion; if not, pick among the remaining tiers
            // 2..PityTier-1 by base-rate proportion. The floor then only ever upgrades a result
            // that pity did not already lift to PityTier+.
            int k = pullsSincePity + 1;
            int pityBp = rules.ComputePityChanceBasisPoints(k);
            int roll1 = rng.NextInt(0, 10000);

            int rolledTier = roll1 < pityBp
                ? PickWeightedTier(rules, rules.PityTier, 5, rng)
                : PickWeightedTier(rules, 2, rules.PityTier - 1, rng);

            if (rolledTier < rules.FloorTier && pullsSinceFloor >= rules.FloorWindowPulls)
            {
                rolledTier = PickWeightedTier(rules, rules.FloorTier, rules.PityTier - 1, rng);
            }

            CharacterData picked = PickFromPool(poolByTier, rolledTier, rng);
            int actualTier = picked.Rarity;

            // Pity/floor react to what the player actually received, not the die roll that may
            // have been folded down to a lower, non-empty tier (docs/12 §"โครงชั้นความหายาก").
            int nextPullsSincePity = actualTier >= rules.PityTier ? 0 : pullsSincePity + 1;
            int nextPullsSinceFloor = actualTier >= rules.FloorTier ? 0 : pullsSinceFloor + 1;

            return new SummonPullOutcome(picked, actualTier, nextPullsSincePity, nextPullsSinceFloor);
        }

        /// <summary>Builds the tier→pullable-stage-I-lines pool a <see cref="ContentPack"/> offers,
        /// same shape <see cref="SummonSimulator"/> uses internally — shared here so a caller (the
        /// server) building this once per request does not have to re-derive the filtering rule
        /// ("stage 1 only", docs/12 "สุ่มได้เฉพาะร่างขั้น 1 เท่านั้น").</summary>
        public static Dictionary<int, List<CharacterData>> BuildPool(ContentPack pack)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            var poolByTier = new Dictionary<int, List<CharacterData>>
            {
                { 2, new List<CharacterData>() },
                { 3, new List<CharacterData>() },
                { 4, new List<CharacterData>() },
                { 5, new List<CharacterData>() }
            };

            foreach (CharacterData character in pack.Characters)
            {
                if (character.EvolveStage == 1 && poolByTier.ContainsKey(character.Rarity))
                {
                    poolByTier[character.Rarity].Add(character);
                }
            }

            foreach (List<CharacterData> lines in poolByTier.Values)
            {
                lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            return poolByTier;
        }

        /// <summary>
        /// Picks a tier in [<paramref name="lowTierInclusive"/>..<paramref name="highTierInclusive"/>]
        /// weighted by base rate, checked highest tier first (so within a pity-fires roll R5 is
        /// checked before R4, matching how a player reads "the pity roll landed on the top slice
        /// first"). A single-tier range needs no roll at all and returns that tier without
        /// consuming any RNG word.
        /// </summary>
        internal static int PickWeightedTier(SummonRules rules, int lowTierInclusive, int highTierInclusive, DeterministicRandom rng)
        {
            if (lowTierInclusive >= highTierInclusive)
            {
                return lowTierInclusive;
            }

            int total = rules.SumRates(lowTierInclusive, highTierInclusive);
            int roll = rng.NextInt(0, total);
            int cumulative = 0;
            for (int tier = highTierInclusive; tier >= lowTierInclusive; tier--)
            {
                cumulative += rules.RateBasisPoints(tier);
                if (roll < cumulative)
                {
                    return tier;
                }
            }

            return lowTierInclusive;
        }

        /// <summary>Resolves a rolled tier to an actual pullable character, folding down through
        /// lower tiers when the rolled one has no pullable stage-I line (docs/12
        /// §"โครงชั้นความหายาก"). Always draws exactly one RNG word to pick within the resolved
        /// tier, even for a single-candidate tier.</summary>
        internal static CharacterData PickFromPool(IReadOnlyDictionary<int, List<CharacterData>> poolByTier, int rolledTier, DeterministicRandom rng)
        {
            for (int tier = rolledTier; tier >= 2; tier--)
            {
                List<CharacterData> pool = poolByTier[tier];
                if (pool.Count > 0)
                {
                    int index = rng.NextInt(0, pool.Count);
                    return pool[index];
                }
            }

            throw new InvalidOperationException(
                "No pullable stage-I line exists at or below rolled tier R" + rolledTier + ".");
        }
    }
}
