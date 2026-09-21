using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Health;
using DarkMyst.Api.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// GET /health (server/DarkMyst.Api/Program.cs, server/DarkMyst.Api/Health/HealthCheck.cs).
    /// The reachable-database case is exercised over real HTTP against the running app (same as
    /// every other endpoint test here); the unreachable case is exercised directly against
    /// <see cref="HealthCheck.CheckAsync"/> with a connection string nothing is listening on —
    /// pointing the live app itself at a broken connection string is not possible to test this way,
    /// because Program.cs runs EF migrations at startup and a genuinely unreachable database fails
    /// that step before the host (and so /health) ever comes up; this exercises the exact same
    /// method the endpoint calls instead, without needing a second host or touching the shared
    /// Postgres cluster the rest of the suite depends on.
    /// </summary>
    public sealed class HealthTests : IClassFixture<ApiTestFixture>
    {
        private readonly ApiTestFixture _fixture;

        public HealthTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Reports_healthy_with_a_reachable_database()
        {
            var response = await _fixture.Client.ApiGet("/health", token: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<HealthDto>(Json.Options);
            Assert.Equal("healthy", body.Status);
            Assert.Equal("up", body.Database);
        }

        [Fact]
        public async Task Reports_unhealthy_when_the_database_is_unreachable()
        {
            // Port 1 on loopback: nothing listens there, so this fails fast (connection refused)
            // rather than timing out slowly against a firewalled host.
            const string unreachable = "Host=127.0.0.1;Port=1;Username=darkmyst;Password=darkmyst_dev;" +
                "Database=darkmyst;Timeout=2";

            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseNpgsql(unreachable)
                .UseSnakeCaseNamingConvention()
                .Options;

            await using var db = new ApiDbContext(options);
            HealthResult result = await HealthCheck.CheckAsync(db, CancellationToken.None);

            Assert.False(result.DatabaseReachable);
            Assert.Equal("down", result.Database);
        }

        private sealed record HealthDto(string Status, string Database);
    }
}
