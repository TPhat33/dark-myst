using System;

namespace DarkMyst.Combat
{
    /// <summary>
    /// PCG-XSH-RR 32-bit generator.
    /// <para>
    /// The battle simulator must never use <see cref="System.Random"/>: its algorithm is not
    /// specified and differs between .NET versions and runtimes, which would make the server
    /// and the Unity client disagree about the outcome of the same battle. This generator is
    /// defined entirely in terms of <see cref="ulong"/> wrap-around arithmetic, so it produces
    /// the same stream everywhere.
    /// </para>
    /// <para>
    /// <see cref="CallCount"/> is part of the battle result. If the client and the server ever
    /// report a different call count for the same seed, the rule sets have drifted apart.
    /// </para>
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong DefaultStream = 0xDA3E39CB94B95BDBUL;

        private readonly ulong _increment;
        private ulong _state;

        public DeterministicRandom(ulong seed, ulong stream = DefaultStream)
        {
            _increment = (stream << 1) | 1UL;
            _state = 0UL;
            Step();
            unchecked { _state += seed; }
            Step();
            CallCount = 0;
        }

        /// <summary>Number of 32-bit words drawn so far. Used for desync detection.</summary>
        public long CallCount { get; private set; }

        /// <summary>Draws the next 32-bit word.</summary>
        public uint NextUInt32()
        {
            CallCount++;
            return Step();
        }

        /// <summary>Draws a uniformly distributed value in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            uint range = (uint)((long)maxExclusive - minInclusive);
            if (range == 1u)
            {
                // Still consume a word so that the RNG stream does not depend on how many
                // candidates a targeting rule happened to find.
                NextUInt32();
                return minInclusive;
            }

            // Rejection sampling: discard the biased tail so every value is equally likely.
            uint threshold = (uint)((0x1_0000_0000UL - range) % range);
            while (true)
            {
                uint draw = NextUInt32();
                if (draw >= threshold)
                {
                    return minInclusive + (int)(draw % range);
                }
            }
        }

        /// <summary>Rolls a chance expressed in per-mille (1000 = always, 0 = never).</summary>
        public bool Chance(int perMille)
        {
            if (perMille >= 1000)
            {
                return true;
            }

            if (perMille <= 0)
            {
                return false;
            }

            return NextInt(0, 1000) < perMille;
        }

        private uint Step()
        {
            unchecked
            {
                ulong previous = _state;
                _state = previous * Multiplier + _increment;

                uint xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
                int rotation = (int)(previous >> 59);
                return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
            }
        }
    }
}
