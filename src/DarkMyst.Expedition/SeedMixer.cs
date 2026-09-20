using System.Text;

namespace DarkMyst.Expedition
{
    /// <summary>
    /// Turns a run seed plus a small integer key into a fresh 64-bit seed, entirely with
    /// <c>ulong</c> wrap-around arithmetic.
    /// <para>
    /// This is what lets one <see cref="ExpeditionRun"/> hand out many independent
    /// <see cref="Combat.DeterministicRandom"/> streams — one for the map layout, one per node
    /// for its battle, one per node for its reward roll — without ever sharing RNG state between
    /// them, and without touching <see cref="System.Random"/> or <c>string.GetHashCode()</c>.
    /// The latter matters as much as the former: .NET randomizes <c>GetHashCode()</c> per
    /// process by default, so two runs of the very same request on two different server
    /// processes would derive two different seeds and desync forever. Everything here reduces
    /// to arithmetic on the UTF-8 bytes instead.
    /// </para>
    /// </summary>
    internal static class SeedMixer
    {
        // Same family of constants as SplitMix64: odd, and chosen for how they scatter bits
        // under multiplication. Nothing here needs to be secret, only stable across platforms.
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
        private const ulong Mix2 = 0x94D049BB133111EBUL;

        /// <summary>Combines a base seed with a small integer key into a new, unrelated seed.</summary>
        public static ulong Mix(ulong baseSeed, ulong key)
        {
            unchecked
            {
                ulong z = baseSeed + GoldenGamma * (key + 1UL);
                z = (z ^ (z >> 30)) * Mix1;
                z = (z ^ (z >> 27)) * Mix2;
                return z ^ (z >> 31);
            }
        }

        /// <summary>
        /// FNV-1a 64 over the UTF-8 bytes of <paramref name="text"/>. Stable across processes and
        /// platforms, unlike <c>string.GetHashCode()</c>, which is exactly why a stage id can
        /// safely fold into a run's seed instead of just its numeric seed value.
        /// </summary>
        public static ulong HashString(string text)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            if (text == null)
            {
                return hash;
            }

            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                unchecked
                {
                    hash ^= b;
                    hash *= prime;
                }
            }

            return hash;
        }
    }
}
