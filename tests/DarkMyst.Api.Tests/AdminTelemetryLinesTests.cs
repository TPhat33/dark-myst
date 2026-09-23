using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Telemetry;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// <c>GET /admin/telemetry/lines</c>'s arithmetic, checked exactly against a small hand-built
    /// fixture built through the real API (docs/10-backend-spec.md's telemetry section). Every
    /// battle outcome and expedition placement below was verified deterministic against the running
    /// server before being hardcoded here (see the session handoff) — this class owns its own
    /// throwaway database (<see cref="ApiTestFixture"/> gives every test class a fresh one) so no
    /// other test's events can shift these totals.
    /// </summary>
    public sealed class AdminTelemetryLinesTests : IClassFixture<ApiTestFixture>
    {
        private const string AshenKnightI = "chr_ashen_knight_i"; // line_ashen_knight
        private const string GraveWardenI = "chr_grave_warden_i"; // line_grave_warden
        private const string TutorialHounds = "enc_tutorial_hounds";
        private const string Boss = "enc_boss_ashen_revenant";
        private const double Epsilon = 1e-9;

        private readonly ApiTestFixture _fixture;

        public AdminTelemetryLinesTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Pick_rate_and_with_vs_without_win_rate_are_computed_exactly()
        {
            var client = _fixture.Client;

            // With-line battles: enc_tutorial_hounds x2 wins (level 20 always wins it), plus
            // enc_boss_ashen_revenant x1 loss (level 1 always loses it) — both deterministic,
            // checked directly against the running server before being fixed here.
            await RunBattleAsync(client, AshenKnightI, level: 20, TutorialHounds);
            await RunBattleAsync(client, AshenKnightI, level: 20, TutorialHounds);
            await RunBattleAsync(client, AshenKnightI, level: 1, Boss);

            // Without-line battles, same encounters, a different line: enc_tutorial_hounds x1 win
            // (level 20) + x2 losses (level 1) — nothing against the boss.
            await RunBattleAsync(client, GraveWardenI, level: 20, TutorialHounds);
            await RunBattleAsync(client, GraveWardenI, level: 1, TutorialHounds);
            await RunBattleAsync(client, GraveWardenI, level: 1, TutorialHounds);

            // Pick rate: 2 of 3 expedition starts on stg_ashfields carry line_ashen_knight.
            await StartExpeditionAsync(client, AshenKnightI, seed: 9001);
            await StartExpeditionAsync(client, AshenKnightI, seed: 9002);
            await StartExpeditionAsync(client, GraveWardenI, seed: 9003);

            (_, string adminToken) = await client.CreateAdminAsync("lines-arithmetic-admin");
            var response = await client.AdminGet("/admin/telemetry/lines", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await response.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);

            TelemetryLineReport ashenKnight = body.Lines.Single(l => l.LineId == "line_ashen_knight");

            TelemetryStagePickRate pickRate = ashenKnight.PickRatesByStage.Single(p => p.StageId == "stg_ashfields");
            Assert.Equal(3, pickRate.TotalStarts);
            Assert.Equal(2, pickRate.StartsWithLine);
            Assert.Equal(2.0 / 3.0, pickRate.PickRate, Epsilon);

            TelemetryWinRateComparison houndsRate = ashenKnight.WinRatesByEncounter.Single(w => w.EncounterId == TutorialHounds);
            Assert.Equal(2, houndsRate.With.N);
            Assert.Equal(1.0, houndsRate.With.WinRate.Value, Epsilon);
            Assert.Equal(20.0, houndsRate.With.MeanTeamLevel.Value, Epsilon);
            Assert.Equal(3, houndsRate.Without.N);
            Assert.Equal(1.0 / 3.0, houndsRate.Without.WinRate.Value, Epsilon);
            Assert.Equal((20.0 + 1.0 + 1.0) / 3.0, houndsRate.Without.MeanTeamLevel.Value, Epsilon);
            Assert.True(houndsRate.LowSample);

            TelemetryWinRateComparison bossRate = ashenKnight.WinRatesByEncounter.Single(w => w.EncounterId == Boss);
            Assert.Equal(1, bossRate.With.N);
            Assert.Equal(0.0, bossRate.With.WinRate.Value, Epsilon);
            Assert.Equal(1.0, bossRate.With.MeanTeamLevel.Value, Epsilon);
            Assert.Equal(0, bossRate.Without.N);
            Assert.Null(bossRate.Without.WinRate);
            Assert.Null(bossRate.Without.MeanTeamLevel);

            Assert.Equal(3, ashenKnight.WinRateOverall.With.N);
            Assert.Equal(2.0 / 3.0, ashenKnight.WinRateOverall.With.WinRate.Value, Epsilon);
            Assert.Equal((20.0 + 20.0 + 1.0) / 3.0, ashenKnight.WinRateOverall.With.MeanTeamLevel.Value, Epsilon);
            Assert.Equal(3, ashenKnight.WinRateOverall.Without.N);
            Assert.Equal(1.0 / 3.0, ashenKnight.WinRateOverall.Without.WinRate.Value, Epsilon);
            Assert.Equal((20.0 + 1.0 + 1.0) / 3.0, ashenKnight.WinRateOverall.Without.MeanTeamLevel.Value, Epsilon);

            // Every grant above (3 for the battles, 2 for the starts) was its own fresh account.
            Assert.Equal(5, ashenKnight.ObtainedCount);
            Assert.Equal(5, ashenKnight.DistinctOwnerCount);
        }

        private static async Task RunBattleAsync(System.Net.Http.HttpClient client, string characterId, int level, string encounterId)
        {
            (_, string token) = await client.CreateGuestAsync();
            OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level);
            var response = await client.ApiPost("/battle/run", token, body: new
            {
                encounterId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = character.InstanceId } }
            });
            response.EnsureSuccessStatusCode();
        }

        private static async Task StartExpeditionAsync(System.Net.Http.HttpClient client, string characterId, ulong seed)
        {
            (_, string token) = await client.CreateGuestAsync();
            OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level: 20);
            await StartExpeditionAsync(client, token, character.InstanceId, seed);
        }

        private static async Task StartExpeditionAsync(System.Net.Http.HttpClient client, string token, string instanceId, ulong seed)
        {
            var response = await client.ApiPost("/expeditions/start", token, "start-" + instanceId, new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId } },
                seed
            });
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Same arithmetic guarantee as <see cref="AdminTelemetryLinesTests"/>, in its own test
    /// class purely so it gets its own throwaway database — both classes write
    /// <c>character_obtained</c> events for <c>line_ashen_knight</c>, and the exact counts each one
    /// asserts would otherwise contaminate each other.</summary>
    public sealed class AdminTelemetryLinesEvolveAndUsageTests : IClassFixture<ApiTestFixture>
    {
        private const string AshenKnightI = "chr_ashen_knight_i";

        private readonly ApiTestFixture _fixture;

        public AdminTelemetryLinesEvolveAndUsageTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Evolve_first_evolve_never_used_and_median_days_to_first_use_are_computed_exactly()
        {
            var client = _fixture.Client;

            // Account X: evolves its first (and only) character on this line.
            (_, string tokenX) = await client.CreateGuestAsync();
            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, tokenX, AshenKnightI, level: 20);
            OwnedCharacter fodder1 = await Seed.GrantCharacterAsync(client, tokenX, AshenKnightI, level: 5);
            OwnedCharacter fodder2 = await Seed.GrantCharacterAsync(client, tokenX, AshenKnightI, level: 5);
            await Seed.GrantGoldAsync(client, tokenX, 10_000);
            await Seed.GrantMaterialAsync(client, tokenX, "mat_ashen_sigil", 5);
            var evolveRequest = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { fodder1.InstanceId, fodder2.InstanceId },
                focus = "Offense"
            };
            var evolveResponse = await client.ApiPost("/evolve/confirm", tokenX, "evolve-" + subject.InstanceId, evolveRequest);
            evolveResponse.EnsureSuccessStatusCode();
            // None of X's three grants (subject + 2 fodder) is ever placed in a team, expedition or
            // battle -> all three count toward "obtained but never used", regardless of the evolve.

            // Account Y: obtained, then placed into an expedition ~5 days later.
            (string accountY, string tokenY) = await client.CreateGuestAsync();
            OwnedCharacter used = await Seed.GrantCharacterAsync(client, tokenY, AshenKnightI, level: 5);
            DateTimeOffset backdatedObtainedAt = DateTimeOffset.UtcNow.AddDays(-5);
            await RawDb.BackdateLatestTelemetryEventAsync(
                _fixture.ConnectionString, accountY, TelemetryEventTypes.CharacterObtained, backdatedObtainedAt);
            await StartExpeditionAsync(client, tokenY, used.InstanceId, seed: 4242);

            // Account Z: obtained, never used at all.
            (_, string tokenZ) = await client.CreateGuestAsync();
            await Seed.GrantCharacterAsync(client, tokenZ, AshenKnightI, level: 5);

            (_, string adminToken) = await client.CreateAdminAsync("lines-evolve-admin");
            var response = await client.AdminGet("/admin/telemetry/lines", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await response.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);
            TelemetryLineReport ashenKnight = body.Lines.Single(l => l.LineId == "line_ashen_knight");

            Assert.Equal(5, ashenKnight.ObtainedCount); // 3 (X) + 1 (Y) + 1 (Z)
            Assert.Equal(3, ashenKnight.DistinctOwnerCount);
            Assert.Equal(1, ashenKnight.EvolveCompletedCount);
            Assert.Equal(1, ashenKnight.FirstEvolveCount);
            Assert.Equal(4, ashenKnight.ObtainedNeverUsedCount); // X's subject + 2 fodder, and Z's

            Assert.NotNull(ashenKnight.MedianDaysObtainedToFirstUse);
            // Y is the only "used" instance, so the median is exactly its own days-to-first-use —
            // ~5 days, with generous slack for how long this test itself takes to run.
            Assert.InRange(ashenKnight.MedianDaysObtainedToFirstUse.Value, 4.9, 5.1);
        }

        private static async Task RunBattleAsync(System.Net.Http.HttpClient client, string characterId, int level, string encounterId)
        {
            (_, string token) = await client.CreateGuestAsync();
            OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level);
            var response = await client.ApiPost("/battle/run", token, body: new
            {
                encounterId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = character.InstanceId } }
            });
            response.EnsureSuccessStatusCode();
        }

        private static async Task StartExpeditionAsync(System.Net.Http.HttpClient client, string characterId, ulong seed)
        {
            (_, string token) = await client.CreateGuestAsync();
            OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level: 20);
            await StartExpeditionAsync(client, token, character.InstanceId, seed);
        }

        private static async Task StartExpeditionAsync(System.Net.Http.HttpClient client, string token, string instanceId, ulong seed)
        {
            var response = await client.ApiPost("/expeditions/start", token, "start-" + instanceId, new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId } },
                seed
            });
            response.EnsureSuccessStatusCode();
        }
    }
}
