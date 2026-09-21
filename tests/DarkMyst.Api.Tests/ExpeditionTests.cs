using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// Server ownership of DarkMyst.Expedition (docs/09-expedition-spec.md's "known limitations"
    /// section, implemented by ExpeditionService — see its type-level remarks) against a live
    /// database: the clone-then-commit pattern, row locking, reward settlement timing, and content
    /// version pinning across a "the latest version moved on" event.
    /// </summary>
    public sealed class ExpeditionTests : IClassFixture<ApiTestFixture>
    {
        private const string StageId = "stg_ashfields";
        private const string LeaderCharacterId = "chr_ashen_knight_i";

        private readonly ApiTestFixture _fixture;

        public ExpeditionTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Starting_locks_the_placed_character_and_abandoning_releases_it_without_choosing()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, LeaderCharacterId, level: 20);

            var startRequest = new
            {
                stageId = StageId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = 20260921UL
            };
            var startResponse = await client.ApiPost("/expeditions/start", token, "start-" + leader.InstanceId, startRequest);
            startResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary summary = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);
            Assert.NotEmpty(summary.AvailableChoices);
            Assert.Null(summary.CurrentNodeId);

            OwnedCharacter leaderDuringRun = await GetCharacterAsync(client, token, leader.InstanceId);
            Assert.True(leaderDuringRun.IsInUse);

            var abandonResponse = await client.ApiPost(
                "/expeditions/" + summary.RunId + "/abandon", token, "abandon-" + summary.RunId);
            abandonResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary afterAbandon = await abandonResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);
            Assert.Equal("Abandoned", afterAbandon.Lifecycle);

            OwnedCharacter leaderAfter = await GetCharacterAsync(client, token, leader.InstanceId);
            Assert.False(leaderAfter.IsInUse);

            // Nothing was banked before abandoning, so nothing should have been granted.
            var meResponse = await client.ApiGet("/accounts/me", token);
            AccountMeDto me = await meResponse.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);
            int gold = await RawDb.GetGoldAsync(_fixture.ConnectionString, me.AccountId);
            Assert.Equal(0, gold);

            var secondAbandon = await client.ApiPost(
                "/expeditions/" + summary.RunId + "/abandon", token, "abandon-again-" + summary.RunId);
            Assert.Equal(HttpStatusCode.Conflict, secondAbandon.StatusCode);
        }

        [Fact]
        public async Task Two_simultaneous_identical_choose_requests_advance_the_run_exactly_once()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, LeaderCharacterId, level: 30);

            var startRequest = new
            {
                stageId = StageId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = 424242UL
            };
            var startResponse = await client.ApiPost("/expeditions/start", token, "start-" + leader.InstanceId, startRequest);
            ExpeditionRunSummary summary = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);

            var chooseRequest = new { choiceIndex = 0 };
            const string key = "concurrent-choose";

            Task<System.Net.Http.HttpResponseMessage> t1 = client.ApiPost(
                "/expeditions/" + summary.RunId + "/choose", token, key, chooseRequest);
            Task<System.Net.Http.HttpResponseMessage> t2 = client.ApiPost(
                "/expeditions/" + summary.RunId + "/choose", token, key, chooseRequest);
            await Task.WhenAll(t1, t2);

            t1.Result.EnsureSuccessStatusCode();
            t2.Result.EnsureSuccessStatusCode();
            Assert.Equal(await t1.Result.Content.ReadAsStringAsync(), await t2.Result.Content.ReadAsStringAsync());

            // The node was resolved exactly once — a duplicated resolution would show up as two
            // log entries (and, for a reward node, banked totals doubled).
            int logLength = await RawDb.GetExpeditionLogLengthAsync(_fixture.ConnectionString, summary.RunId);
            Assert.Equal(1, logLength);
        }

        [Fact]
        public async Task Resuming_a_run_pinned_to_a_content_version_the_server_never_registered_is_refused()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            string runId = Guid.NewGuid().ToString("n");
            await RawDb.InsertOrphanedExpeditionRunAsync(_fixture.ConnectionString, runId, accountId, "9.9.9-retired");

            var response = await client.ApiGet("/expeditions/" + runId, token);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ErrorDto>(Json.Options);
            Assert.Equal("content_version_unavailable", body.Error);
        }

        private static async Task<OwnedCharacter> GetCharacterAsync(
            System.Net.Http.HttpClient client, string token, string instanceId)
        {
            var response = await client.ApiGet("/debug/character/" + instanceId, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OwnedCharacter>(Json.Options);
        }

        private sealed record AccountMeDto(string AccountId, string Kind, int Gold, object Summary);

        private sealed record ErrorDto(string Error, string Version);
    }

    /// <summary>
    /// Exercises docs/09-expedition-spec.md's version-pinning rule the way only a server with two
    /// content versions resident at once can: a run started under the version that was "latest" at
    /// the time keeps resolving under it even after a newer version becomes latest for everyone
    /// else — see MultiVersionApiTestFixture.
    /// </summary>
    public sealed class ExpeditionContentVersionPinningTests : IClassFixture<MultiVersionApiTestFixture>
    {
        private readonly MultiVersionApiTestFixture _fixture;

        public ExpeditionContentVersionPinningTests(MultiVersionApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task A_run_pinned_to_the_original_version_keeps_resolving_after_latest_moves_on()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 20);

            var startRequest = new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = 7UL
            };
            var startResponse = await client.ApiPost("/expeditions/start", token, "start-1", startRequest);
            startResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary original = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);
            Assert.Equal(ContentVersions.OriginalVersion, original.ContentVersion);

            // Content moves on — new grants and new runs pin to the bumped version from here on.
            var registry = _fixture.Factory.Services.GetRequiredService<ContentPackRegistry>();
            registry.SetLatest(ContentVersions.BumpedVersion);

            // The original run, pinned to the version it began with, must still resolve — the
            // whole point of docs/05-content-pipeline.md's "เซิร์ฟเวอร์เก็บ content หลายเวอร์ชัน
            // พร้อมกัน" and docs/09-expedition-spec.md's version-pinning rule.
            var getResponse = await client.ApiGet("/expeditions/" + original.RunId, token);
            getResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary reread = await getResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);
            Assert.Equal(ContentVersions.OriginalVersion, reread.ContentVersion);
            Assert.NotEmpty(reread.AvailableChoices);

            // A freshly started run, meanwhile, pins to whatever is latest now.
            OwnedCharacter leader2 = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 20);
            var secondStart = new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader2.InstanceId } },
                seed = 8UL
            };
            var secondResponse = await client.ApiPost("/expeditions/start", token, "start-2", secondStart);
            secondResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary second = await secondResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);
            Assert.Equal(ContentVersions.BumpedVersion, second.ContentVersion);
        }
    }
}
