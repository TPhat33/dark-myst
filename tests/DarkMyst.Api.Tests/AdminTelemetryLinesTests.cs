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

        // Deterministic seed: choiceIndex 0 and 1 are both Battle nodes against
        // enc_tutorial_hounds at this seed (map generation only depends on the seed, not on the
        // character placed) — verified against the running server before being hardcoded here.
        private const ulong BattleSeed = 24;
        private const int WinChoiceIndex = 0; // level 20 always wins here
        private const int LossChoiceIndexForLevel1 = 1; // level 1 always loses here

        private const double Epsilon = 1e-9;

        private readonly ApiTestFixture _fixture;

        public AdminTelemetryLinesTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Pick_rate_and_with_vs_without_win_rate_are_computed_exactly_from_expedition_battles_only()
        {
            var client = _fixture.Client;

            // With-line: 2 expedition battles, both wins (level 20, WinChoiceIndex).
            await ExpeditionBattleAsync(client, AshenKnightI, level: 20, WinChoiceIndex);
            await ExpeditionBattleAsync(client, AshenKnightI, level: 20, WinChoiceIndex);

            // Without-line: 3 expedition battles, 1 win (level 20) + 2 losses (level 1).
            await ExpeditionBattleAsync(client, GraveWardenI, level: 20, WinChoiceIndex);
            await ExpeditionBattleAsync(client, GraveWardenI, level: 1, LossChoiceIndexForLevel1);
            await ExpeditionBattleAsync(client, GraveWardenI, level: 1, LossChoiceIndexForLevel1);

            (_, string adminToken) = await client.CreateAdminAsync("lines-arithmetic-admin");
            var response = await client.AdminGet("/admin/telemetry/lines", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await response.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);

            TelemetryLineReport ashenKnight = body.Lines.Single(l => l.LineId == "line_ashen_knight");

            // Each expedition battle above is itself one expedition start on stg_ashfields, so pick
            // rate is 2 (ashen_knight) of 5 (2 + 3) total starts.
            TelemetryStagePickRate pickRate = ashenKnight.PickRatesByStage.Single(p => p.StageId == "stg_ashfields");
            Assert.Equal(5, pickRate.TotalStarts);
            Assert.Equal(2, pickRate.StartsWithLine);
            Assert.Equal(2.0 / 5.0, pickRate.PickRate, Epsilon);

            TelemetryWinRateComparison houndsRate = ashenKnight.WinRatesByEncounter.Single(w => w.EncounterId == TutorialHounds);
            Assert.Equal(2, houndsRate.With.N);
            Assert.Equal(2, houndsRate.With.Accounts);
            Assert.Equal(1.0, houndsRate.With.WinRate.Value, Epsilon);
            Assert.Equal(20.0, houndsRate.With.MeanTeamLevel.Value, Epsilon);
            Assert.Equal(3, houndsRate.Without.N);
            Assert.Equal(3, houndsRate.Without.Accounts);
            Assert.Equal(1.0 / 3.0, houndsRate.Without.WinRate.Value, Epsilon);
            Assert.Equal((20.0 + 1.0 + 1.0) / 3.0, houndsRate.Without.MeanTeamLevel.Value, Epsilon);
            // Low sample two ways here: n < 30 on both sides, and also < 5 distinct accounts.
            Assert.True(houndsRate.LowSample);

            Assert.Equal(2, ashenKnight.WinRateOverall.With.N);
            Assert.Equal(1.0, ashenKnight.WinRateOverall.With.WinRate.Value, Epsilon);
            Assert.Equal(3, ashenKnight.WinRateOverall.Without.N);
            Assert.Equal(1.0 / 3.0, ashenKnight.WinRateOverall.Without.WinRate.Value, Epsilon);

            // Every grant above (2 for the with-line battles, 3 for the without-line battles) was
            // its own fresh account.
            Assert.Equal(2, ashenKnight.ObtainedCount);
            Assert.Equal(2, ashenKnight.DistinctOwnerCount);
        }

        /// <summary>Builds one account, grants it a fresh character, starts an expedition on
        /// <c>stg_ashfields</c> with <see cref="BattleSeed"/> and resolves the Battle node at
        /// <paramref name="choiceIndex"/> — the only way this test suite produces a
        /// <c>battle_finished</c> event with <c>context: "expedition"</c>.</summary>
        internal static async Task ExpeditionBattleAsync(
            System.Net.Http.HttpClient client, string characterId, int level, int choiceIndex)
        {
            (_, string token) = await client.CreateGuestAsync();
            OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level);
            var start = await client.ApiPost("/expeditions/start", token, "start-" + character.InstanceId, new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = character.InstanceId } },
                seed = BattleSeed
            });
            start.EnsureSuccessStatusCode();
            var runId = (await start.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options)).GetProperty("runId").GetString();

            var choose = await client.ApiPost(
                "/expeditions/" + runId + "/choose", token, "choose-" + runId, new { choiceIndex });
            choose.EnsureSuccessStatusCode();
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

    /// <summary>Proves item 1 of the review: <c>/battle/run</c> (context <c>battle_run</c>) must
    /// never move a line's win rate, however many times it is replayed — that sandbox is free,
    /// unlimited retries with no reward, so one player spamming it could otherwise dominate the
    /// "with vs without" comparison. Own database, same reasoning as every other class here.</summary>
    public sealed class AdminTelemetryLinesBattleRunExclusionTests : IClassFixture<ApiTestFixture>
    {
        private const string AshenKnightI = "chr_ashen_knight_i";
        private const string TutorialHounds = "enc_tutorial_hounds";
        private const double Epsilon = 1e-9;

        private readonly ApiTestFixture _fixture;

        public AdminTelemetryLinesBattleRunExclusionTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Flooding_battle_run_wins_does_not_move_the_expedition_win_rate()
        {
            var client = _fixture.Client;

            // Baseline, from real expedition battles: 1 win + 1 loss -> win rate exactly 0.5.
            await AdminTelemetryLinesTests.ExpeditionBattleAsync(client, AshenKnightI, level: 20, choiceIndex: 0);
            await AdminTelemetryLinesTests.ExpeditionBattleAsync(client, AshenKnightI, level: 1, choiceIndex: 1);

            // Flood: 20 training-ground wins for the same line against the same encounter.
            for (int i = 0; i < 20; i++)
            {
                (_, string token) = await client.CreateGuestAsync();
                OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);
                var response = await client.ApiPost("/battle/run", token, body: new
                {
                    encounterId = TutorialHounds,
                    leaderSlot = 0,
                    placements = new[] { new { slot = 0, instanceId = character.InstanceId } }
                });
                response.EnsureSuccessStatusCode();
            }

            (_, string adminToken) = await client.CreateAdminAsync("battle-run-flood-admin");
            var linesResponse = await client.AdminGet("/admin/telemetry/lines", adminToken);
            linesResponse.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await linesResponse.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);
            TelemetryLineReport ashenKnight = body.Lines.Single(l => l.LineId == "line_ashen_knight");

            TelemetryWinRateComparison houndsRate = ashenKnight.WinRatesByEncounter.Single(w => w.EncounterId == TutorialHounds);
            Assert.Equal(2, houndsRate.With.N); // still just the 2 expedition battles
            Assert.Equal(0.5, houndsRate.With.WinRate.Value, Epsilon);
            Assert.Equal(2, ashenKnight.WinRateOverall.With.N);
            Assert.Equal(0.5, ashenKnight.WinRateOverall.With.WinRate.Value, Epsilon);

            // The flood is still raw data: it counts for "obtained but never used" purposes (every
            // flooded character was placed in a battle_run fight, so none of the 22 grants here
            // ends up in ObtainedNeverUsedCount).
            Assert.Equal(0, ashenKnight.ObtainedNeverUsedCount);
        }
    }

    /// <summary>Proves item 2 of the review: a win-rate group with <c>n &gt;= 30</c> battles but
    /// fewer than 5 distinct accounts behind them is still <c>lowSample</c> — a handful of heavy
    /// players cannot make a comparison look trustworthy by replaying the same fight. Own database,
    /// same reasoning as every other class here.</summary>
    public sealed class AdminTelemetryLinesSampleSizeTests : IClassFixture<ApiTestFixture>
    {
        private const string AshenKnightI = "chr_ashen_knight_i";
        private const string GraveWardenI = "chr_grave_warden_i";
        private const string TutorialHounds = "enc_tutorial_hounds";

        private readonly ApiTestFixture _fixture;

        public AdminTelemetryLinesSampleSizeTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Thirty_plus_battles_from_under_five_accounts_is_still_low_sample()
        {
            var client = _fixture.Client;

            // 3 accounts, 10 expedition battles each, all wins -> n = 30, accounts = 3 (< 5).
            await RunManyAsync(client, AshenKnightI, accounts: 3, battlesPerAccount: 10);
            // Same shape for the "without" side, so neither side's n < 30 trips the old rule and
            // only the new distinct-account rule is left to explain the low-sample flag.
            await RunManyAsync(client, GraveWardenI, accounts: 3, battlesPerAccount: 10);

            (_, string adminToken) = await client.CreateAdminAsync("sample-size-admin");
            var response = await client.AdminGet("/admin/telemetry/lines", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await response.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);
            TelemetryLineReport ashenKnight = body.Lines.Single(l => l.LineId == "line_ashen_knight");

            TelemetryWinRateComparison houndsRate = ashenKnight.WinRatesByEncounter.Single(w => w.EncounterId == TutorialHounds);
            Assert.Equal(30, houndsRate.With.N);
            Assert.Equal(3, houndsRate.With.Accounts);
            Assert.Equal(30, houndsRate.Without.N);
            Assert.Equal(3, houndsRate.Without.Accounts);
            Assert.True(houndsRate.LowSample); // neither n is < 30, only accounts < 5 explains this
        }

        /// <summary>Runs <paramref name="battlesPerAccount"/> expedition-context wins per account,
        /// across <paramref name="accounts"/> accounts, reusing each account for a fresh character
        /// and a fresh run every time (a character already placed in an active run cannot be reused
        /// until that run ends, so each battle needs its own grant).</summary>
        private static async Task RunManyAsync(
            System.Net.Http.HttpClient client, string characterId, int accounts, int battlesPerAccount)
        {
            for (int a = 0; a < accounts; a++)
            {
                (_, string token) = await client.CreateGuestAsync();
                for (int b = 0; b < battlesPerAccount; b++)
                {
                    OwnedCharacter character = await Seed.GrantCharacterAsync(client, token, characterId, level: 20);
                    var start = await client.ApiPost("/expeditions/start", token, "start-" + character.InstanceId, new
                    {
                        stageId = "stg_ashfields",
                        leaderSlot = 0,
                        placements = new[] { new { slot = 0, instanceId = character.InstanceId } },
                        seed = 24UL
                    });
                    start.EnsureSuccessStatusCode();
                    var runId = (await start.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Json.Options)).GetProperty("runId").GetString();

                    var choose = await client.ApiPost(
                        "/expeditions/" + runId + "/choose", token, "choose-" + runId, new { choiceIndex = 0 });
                    choose.EnsureSuccessStatusCode();
                }
            }
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
