using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Admin;
using DarkMyst.Api.Tests.Infra;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// The admin sweep button (docs/11-admin-spec.md), run against the real API and the real
    /// shipped content — not a mock battle loop. Covers the two bounds
    /// <see cref="DarkMyst.Api.Admin.AdminSweepService"/> enforces: a repeat cap and "one sweep at
    /// a time" — plus that a player bearer token cannot reach it, same as every other admin
    /// endpoint.
    /// </summary>
    public sealed class AdminSweepTests : IClassFixture<AdminApiTestFixture>
    {
        private static readonly List<string> DefaultRoster = new List<string>
        {
            "chr_ashen_knight_i",
            "chr_grave_warden_i",
            "chr_ember_adept_i",
            "chr_tide_oracle_i",
            "chr_pale_stalker_i"
        };

        private readonly AdminApiTestFixture _fixture;

        public AdminSweepTests(AdminApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Sweep_returns_real_win_rate_and_survival_numbers()
        {
            (_, string token) = await _fixture.Client.CreateAdminAsync("sweep-admin-1");

            var response = await _fixture.Client.AdminPost("/admin/content/sweep", token, body: new
            {
                encounterId = "enc_tutorial_hounds",
                roster = DefaultRoster,
                level = 20,
                seed = 1,
                repeat = 25
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<AdminSweepResponseDto>(Json.Options);
            Assert.Equal("enc_tutorial_hounds", result.EncounterId);
            Assert.Equal(25, result.Battles);
            Assert.InRange(result.Wins, 0, 25);
            Assert.True(result.AverageRounds > 0);
            Assert.NotEmpty(result.Survivors);
            Assert.True(result.MaxRepeat > 0);
        }

        [Fact]
        public async Task Sweep_above_the_repeat_cap_is_refused_before_running_anything()
        {
            (_, string token) = await _fixture.Client.CreateAdminAsync("sweep-admin-2");

            var response = await _fixture.Client.AdminPost("/admin/content/sweep", token, body: new
            {
                encounterId = "enc_tutorial_hounds",
                roster = DefaultRoster,
                repeat = 5000
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<SweepTooLargeDto>(Json.Options);
            Assert.Equal("sweep_repeat_too_large", problem.Error);
            Assert.Equal(5000, problem.Requested);
            Assert.True(problem.Max < 5000);
        }

        /// <summary>
        /// Calls <see cref="AdminSweepService"/> directly (bypassing HTTP) so this is
        /// deterministic rather than a timing race: an unawaited call to an async method runs
        /// synchronously up to its first genuine suspension point, which for
        /// <c>AdminSweepService.RunAsync</c> is the <c>Task.Run</c> that hands the battle loop to
        /// the thread pool — by the time that call returns a pending <see cref="Task"/>, the
        /// process-wide lock is already held. A second call made right after that, still without
        /// awaiting the first, then hits <c>SemaphoreSlim.WaitAsync(TimeSpan.Zero)</c> while the
        /// lock is held, which resolves synchronously to "not acquired" — no sleep, no polling,
        /// no flake window.
        /// </summary>
        [Fact]
        public async Task A_second_concurrent_sweep_is_refused_while_one_is_running()
        {
            using IServiceScope scope = _fixture.Factory.Services.CreateScope();
            AdminSweepService service = scope.ServiceProvider.GetRequiredService<AdminSweepService>();

            var request = new AdminSweepRequest("enc_tutorial_hounds", DefaultRoster, Repeat: 50);

            Task<DarkMyst.Sim.SweepResult> first = service.RunAsync(request, CancellationToken.None);

            await Assert.ThrowsAsync<SweepAlreadyRunningException>(
                () => service.RunAsync(request, CancellationToken.None));

            DarkMyst.Sim.SweepResult firstResult = await first;
            Assert.Equal(50, firstResult.Battles);
        }

        [Fact]
        public async Task Sweep_rejects_an_unknown_encounter_id_as_content_invalid()
        {
            (_, string token) = await _fixture.Client.CreateAdminAsync("sweep-admin-4");

            var response = await _fixture.Client.AdminPost("/admin/content/sweep", token, body: new
            {
                encounterId = "enc_does_not_exist",
                roster = DefaultRoster,
                repeat = 5
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        [Fact]
        public async Task A_player_bearer_token_cannot_reach_the_sweep_endpoint()
        {
            // Same structural proof AdminAuthTests already runs for every other admin endpoint:
            // a real, valid player token, sent as the admin header, still is not found in
            // admin_accounts (server/DarkMyst.Api/Auth/AdminAuth.cs).
            (_, string playerToken) = await _fixture.Client.CreateGuestAsync();

            var response = await _fixture.Client.AdminPost("/admin/content/sweep", playerToken, body: new
            {
                encounterId = "enc_tutorial_hounds",
                roster = DefaultRoster,
                repeat = 1
            });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        private sealed record AdminSweepSurvivorEntryDto(string CharacterId, int Survived);

        private sealed record AdminSweepResponseDto(
            string EncounterId, int Battles, int Wins, int Draws, double AverageRounds,
            List<AdminSweepSurvivorEntryDto> Survivors, int MaxRepeat);

        private sealed record SweepTooLargeDto(string Error, string Message, int Requested, int Max);

        private sealed record SweepConflictDto(string Error, string Message);
    }
}
