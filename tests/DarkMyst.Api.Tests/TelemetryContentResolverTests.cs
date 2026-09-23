using DarkMyst.Api.Content;
using DarkMyst.Api.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// <see cref="TelemetryContentResolver"/> in isolation — no database needed. Covers item 3 of
    /// the telemetry review: when the event's own content version cannot be loaded, the resolver
    /// must return a null line rather than silently falling back to whatever is <c>Latest</c> right
    /// now, which would stamp one version's event with another version's line lookup.
    /// </summary>
    public sealed class TelemetryContentResolverTests
    {
        [Fact]
        public void Unresolvable_content_version_yields_a_null_line_not_a_fallback_to_latest()
        {
            string contentDirectory = ContentLocator.Locate(new ConfigurationBuilder().Build());
            ContentPackRegistry registry = ContentPackRegistry.LoadSingleDirectory(contentDirectory, NullLogger<ContentPackRegistry>.Instance);

            // Sanity check: this character id does resolve to a real line under the only version
            // this registry actually has loaded.
            TelemetryContentResolver.LineInfo underLoadedVersion =
                TelemetryContentResolver.Resolve(registry, registry.LatestVersion, "chr_ashen_knight_i");
            Assert.Equal("line_ashen_knight", underLoadedVersion.LineId);

            // A content version this registry never loaded (never even close to "retired" —
            // ContentPackRegistry does not retire anything in this round, see its own remarks —
            // just a version that was never registered at all) must not fall back to Latest.
            TelemetryContentResolver.LineInfo underUnknownVersion =
                TelemetryContentResolver.Resolve(registry, "9.9.9-unknown", "chr_ashen_knight_i");
            Assert.Null(underUnknownVersion.LineId);
            Assert.Equal(0, underUnknownVersion.Rarity);
            Assert.Equal(0, underUnknownVersion.EvolveStage);
        }

        [Fact]
        public void Unknown_character_id_under_a_loaded_version_also_yields_a_null_line()
        {
            string contentDirectory = ContentLocator.Locate(new ConfigurationBuilder().Build());
            ContentPackRegistry registry = ContentPackRegistry.LoadSingleDirectory(contentDirectory, NullLogger<ContentPackRegistry>.Instance);

            TelemetryContentResolver.LineInfo info =
                TelemetryContentResolver.Resolve(registry, registry.LatestVersion, "chr_does_not_exist");
            Assert.Null(info.LineId);
        }
    }
}
