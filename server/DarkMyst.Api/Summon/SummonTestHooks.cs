using System;

namespace DarkMyst.Api.Summon
{
    /// <summary>
    /// TEST-ONLY. Production summon resolution seeds its RNG from
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/> (see
    /// <c>SummonService.GenerateSeed</c>) precisely so pulls are unpredictable per account — no
    /// production code path ever reads <see cref="SeedOverride"/>. It exists only so
    /// <c>tests/DarkMyst.Api.Tests</c> (granted access via this project's
    /// <c>InternalsVisibleTo(DarkMyst.Api.Tests)</c>) can pin a specific seed and assert exact
    /// tiers/lines/shard counts instead of asserting only "some outcome happened". A test that
    /// forgets to clear this after itself would leak into later tests in the same process, so
    /// every test that sets it must reset it to <c>null</c> in a <c>finally</c>/<c>using</c>.
    /// </summary>
    public static class SummonTestHooks
    {
        public static Func<ulong> SeedOverride { get; set; }
    }
}
