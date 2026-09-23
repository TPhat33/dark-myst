using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Telemetry;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// Server-side character telemetry (docs/10-backend-spec.md's telemetry section,
    /// docs/12-summon-spec.md "Telemetry ระดับตัวละคร"): every write site emits the right event
    /// with the right payload and versions, a refused/replayed mutation never emits a duplicate,
    /// and the admin surface is gated the same way every other /admin/* route is.
    /// </summary>
    public sealed class TelemetryTests : IClassFixture<ApiTestFixture>
    {
        private const string AshenKnightI = "chr_ashen_knight_i"; // line_ashen_knight, stage 1
        private const string StageId = "stg_ashfields";
        private const string TutorialHoundsEncounterId = "enc_tutorial_hounds";

        // Deterministic seed: node 0/1 are Battle (enc_tutorial_hounds), node 2 is Treasure whose
        // reward roll grants chr_pale_stalker_i — found by brute-force search (see the session
        // handoff), fixed here so this test never depends on production RNG shifting under it.
        private const ulong TreasureCharacterSeed = 24;

        private readonly ApiTestFixture _fixture;

        public TelemetryTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Debug_grant_writes_a_character_obtained_event_with_correct_payload_and_versions()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            OwnedCharacter granted = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 5);

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.CharacterObtained);
            CharacterObtainedPayload payload = Deserialize<CharacterObtainedPayload>(evt.Payload);

            Assert.Equal("0.4.0", evt.ContentVersion);
            Assert.Equal("1.0.0", evt.RulesVersion);
            Assert.Equal(granted.InstanceId, payload.InstanceId);
            Assert.Equal(AshenKnightI, payload.CharacterId);
            Assert.Equal("line_ashen_knight", payload.LineId);
            Assert.Equal(3, payload.Rarity);
            Assert.Equal(1, payload.EvolveStage);
            Assert.Equal(CharacterObtainedSource.Debug, payload.Source);
            Assert.Null(payload.RunId);
            Assert.Null(payload.StageId);
        }

        [Fact]
        public async Task Starting_an_expedition_writes_an_expedition_started_event_with_the_placed_members()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20, focus: "Offense");

            var startResponse = await client.ApiPost("/expeditions/start", token, "start-" + leader.InstanceId, new
            {
                stageId = StageId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = TreasureCharacterSeed
            });
            startResponse.EnsureSuccessStatusCode();

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.ExpeditionStarted);
            ExpeditionStartedPayload payload = Deserialize<ExpeditionStartedPayload>(evt.Payload);

            Assert.Equal(StageId, payload.StageId);
            TelemetryMemberSnapshot member = Assert.Single(payload.Members);
            Assert.Equal(leader.InstanceId, member.InstanceId);
            Assert.Equal(AshenKnightI, member.CharacterId);
            Assert.Equal("line_ashen_knight", member.LineId);
            Assert.Equal(1, member.EvolveStage);
            Assert.Equal(20, member.Level);
            Assert.Equal("Offense", member.Focus);
        }

        [Fact]
        public async Task An_expedition_node_battle_writes_a_battle_finished_event_with_context_expedition()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);

            var startResponse = await client.ApiPost("/expeditions/start", token, "start-" + leader.InstanceId, new
            {
                stageId = StageId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = TreasureCharacterSeed
            });
            ExpeditionRunSummary summary = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);

            // choiceIndex 0 at this seed is a Battle node against enc_tutorial_hounds, and this
            // leader wins it (verified against the running server; see the handoff for the search).
            var chooseResponse = await client.ApiPost(
                "/expeditions/" + summary.RunId + "/choose", token, "choose-" + summary.RunId, new { choiceIndex = 0 });
            chooseResponse.EnsureSuccessStatusCode();

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.BattleFinished);
            BattleFinishedPayload payload = Deserialize<BattleFinishedPayload>(evt.Payload);

            Assert.Equal("expedition", payload.Context);
            Assert.Equal(summary.RunId, payload.RunId);
            Assert.Equal(StageId, payload.StageId);
            Assert.Equal(TutorialHoundsEncounterId, payload.EncounterId);
            Assert.Equal("win", payload.Outcome);
            Assert.True(payload.Turns > 0);
            TelemetryMemberSnapshot member = Assert.Single(payload.Members);
            Assert.Equal(leader.InstanceId, member.InstanceId);
            Assert.Equal("line_ashen_knight", member.LineId);
        }

        [Fact]
        public async Task An_expedition_treasure_character_reward_writes_a_character_obtained_event_sourced_from_expedition()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);

            var startResponse = await client.ApiPost("/expeditions/start", token, "start-" + leader.InstanceId, new
            {
                stageId = StageId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } },
                seed = TreasureCharacterSeed
            });
            ExpeditionRunSummary summary = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);

            // choiceIndex 2 at this seed is the Treasure node whose reward roll is a character
            // (chr_pale_stalker_i). Rewards are only granted once the run stops being played, so
            // abandon right after to force settlement (docs/10-backend-spec.md).
            var chooseResponse = await client.ApiPost(
                "/expeditions/" + summary.RunId + "/choose", token, "choose-" + summary.RunId, new { choiceIndex = 2 });
            chooseResponse.EnsureSuccessStatusCode();

            var abandonResponse = await client.ApiPost(
                "/expeditions/" + summary.RunId + "/abandon", token, "abandon-" + summary.RunId);
            abandonResponse.EnsureSuccessStatusCode();

            List<TelemetryEventDto> events = await GetEventsAsync(accountId, TelemetryEventTypes.CharacterObtained);
            TelemetryEventDto evt = Assert.Single(events.Where(e =>
                Deserialize<CharacterObtainedPayload>(e.Payload).Source == CharacterObtainedSource.Expedition));
            CharacterObtainedPayload payload = Deserialize<CharacterObtainedPayload>(evt.Payload);

            Assert.Equal("chr_pale_stalker_i", payload.CharacterId);
            Assert.Equal(CharacterObtainedSource.Expedition, payload.Source);
            Assert.Equal(summary.RunId, payload.RunId);
            Assert.Equal(StageId, payload.StageId);
            Assert.NotNull(payload.InstanceId);
        }

        [Fact]
        public async Task Training_ground_battle_writes_a_battle_finished_event_with_context_battle_run()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter leader = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);

            var response = await client.ApiPost("/battle/run", token, body: new
            {
                encounterId = TutorialHoundsEncounterId,
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = leader.InstanceId } }
            });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options);
            string engineOutcome = body.GetProperty("result").GetProperty("outcome").GetString();

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.BattleFinished);
            BattleFinishedPayload payload = Deserialize<BattleFinishedPayload>(evt.Payload);

            Assert.Equal("battle_run", payload.Context);
            Assert.Null(payload.RunId);
            Assert.Null(payload.StageId);
            Assert.Equal(TutorialHoundsEncounterId, payload.EncounterId);
            Assert.Equal(engineOutcome == "AttackerVictory" ? "win" : "loss", payload.Outcome);
        }

        [Fact]
        public async Task Evolve_confirm_writes_an_evolve_completed_event_with_correct_payload()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);
            OwnedCharacter fodder1 = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 5);
            OwnedCharacter fodder2 = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 5);
            await Seed.GrantGoldAsync(client, token, 10_000);
            await Seed.GrantMaterialAsync(client, token, "mat_ashen_sigil", 5);

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { fodder1.InstanceId, fodder2.InstanceId },
                focus = "Offense"
            };
            var confirmResponse = await client.ApiPost("/evolve/confirm", token, "evolve-" + subject.InstanceId, request);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.EvolveCompleted);
            EvolveCompletedPayload payload = Deserialize<EvolveCompletedPayload>(evt.Payload);

            Assert.Equal(subject.InstanceId, payload.InstanceId);
            Assert.Equal(AshenKnightI, payload.FromCharacterId);
            Assert.Equal("chr_ashen_knight_ii", payload.ToCharacterId);
            Assert.Equal("line_ashen_knight", payload.LineId);
            Assert.Equal(1, payload.FromStage);
            Assert.Equal(2, payload.ToStage);
            Assert.Equal(2, payload.SameLineMaterialCount);
            Assert.Equal(
                new[] { fodder1.InstanceId, fodder2.InstanceId }.OrderBy(x => x),
                payload.MaterialInstanceIds.OrderBy(x => x));
        }

        [Fact]
        public async Task A_refused_evolve_writes_no_event()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            // Level 20 but no gold/materials/fodder granted at all -> refused on every blocker.
            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);

            var request = new { subjectInstanceId = subject.InstanceId, fodderInstanceIds = Array.Empty<string>(), focus = (string)null };
            var confirmResponse = await client.ApiPost("/evolve/confirm", token, "evolve-refused-" + subject.InstanceId, request);
            Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

            int count = await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.EvolveCompleted);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Replaying_the_same_evolve_confirm_request_does_not_duplicate_the_event()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 20);
            OwnedCharacter fodder1 = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 5);
            OwnedCharacter fodder2 = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 5);
            await Seed.GrantGoldAsync(client, token, 10_000);
            await Seed.GrantMaterialAsync(client, token, "mat_ashen_sigil", 5);

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { fodder1.InstanceId, fodder2.InstanceId },
                focus = "Offense"
            };
            const string key = "evolve-replay";

            var first = await client.ApiPost("/evolve/confirm", token, key, request);
            first.EnsureSuccessStatusCode();
            var second = await client.ApiPost("/evolve/confirm", token, key, request);
            second.EnsureSuccessStatusCode();
            Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());

            int count = await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.EvolveCompleted);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Saving_a_team_writes_a_team_saved_event()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter member = await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 10);

            var response = await client.ApiPost("/teams", token, "save-" + member.InstanceId, new
            {
                name = "My Team",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = member.InstanceId } }
            });
            response.EnsureSuccessStatusCode();
            var teamId = (await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options)).GetProperty("id").GetString();

            TelemetryEventDto evt = await GetSingleEventAsync(accountId, TelemetryEventTypes.TeamSaved);
            TeamSavedPayload payload = Deserialize<TeamSavedPayload>(evt.Payload);

            Assert.Equal(teamId, payload.TeamId);
            TelemetryMemberSnapshot snapshot = Assert.Single(payload.Members);
            Assert.Equal(member.InstanceId, snapshot.InstanceId);
            Assert.Equal("line_ashen_knight", snapshot.LineId);
        }

        [Fact]
        public async Task Admin_telemetry_events_rejects_a_player_token_and_anonymous()
        {
            var client = _fixture.Client;
            (_, string playerToken) = await client.CreateGuestAsync();

            var withPlayerToken = await client.AdminGet("/admin/telemetry/events", playerToken);
            Assert.Equal(HttpStatusCode.Unauthorized, withPlayerToken.StatusCode);

            var anonymous = await client.AdminGet("/admin/telemetry/events", adminToken: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        [Fact]
        public async Task Admin_telemetry_lines_rejects_a_player_token_and_anonymous()
        {
            var client = _fixture.Client;
            (_, string playerToken) = await client.CreateGuestAsync();

            var withPlayerToken = await client.AdminGet("/admin/telemetry/lines", playerToken);
            Assert.Equal(HttpStatusCode.Unauthorized, withPlayerToken.StatusCode);

            var anonymous = await client.AdminGet("/admin/telemetry/lines", adminToken: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        [Fact]
        public async Task Admin_telemetry_lines_accepts_a_real_admin_token_and_echoes_the_content_version()
        {
            var client = _fixture.Client;
            (_, string adminToken) = await client.CreateAdminAsync();

            var response = await client.AdminGet("/admin/telemetry/lines", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryLinesResponse body = await response.Content.ReadFromJsonAsync<TelemetryLinesResponse>(Json.Options);

            Assert.Equal(new[] { "0.4.0" }, body.ContentVersions);
            Assert.Contains(body.Lines, l => l.LineId == "line_ashen_knight");
        }

        [Fact]
        public async Task Admin_events_pagination_covers_every_event_exactly_once()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            const int grants = 7;
            for (int i = 0; i < grants; i++)
            {
                await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 1);
            }

            (_, string adminToken) = await client.CreateAdminAsync("pagination-admin");

            var seenIds = new List<long>();
            long? after = null;
            for (int guard = 0; guard < grants + 2; guard++)
            {
                string path = "/admin/telemetry/events?type=" + TelemetryEventTypes.CharacterObtained + "&limit=3"
                    + (after.HasValue ? "&after=" + after.Value : string.Empty);
                var response = await client.AdminGet(path, adminToken);
                response.EnsureSuccessStatusCode();
                TelemetryEventsResponse page = await response.Content.ReadFromJsonAsync<TelemetryEventsResponse>(Json.Options);

                var thisAccountItems = page.Items.Where(i => i.AccountId == accountId).ToList();
                seenIds.AddRange(thisAccountItems.Select(i => i.Id));

                if (page.NextAfter == null)
                {
                    break;
                }

                after = page.NextAfter;
            }

            Assert.Equal(grants, seenIds.Count);
            Assert.Equal(seenIds.Distinct().Count(), seenIds.Count); // no dupes
            Assert.Equal(seenIds.OrderBy(x => x), seenIds); // no gaps in ordering either
        }

        [Fact]
        public async Task Deleting_an_account_cascades_to_its_telemetry_events()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantCharacterAsync(client, token, AshenKnightI, level: 1);

            int before = await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId);
            Assert.True(before > 0);

            await RawDb.DeleteAccountAsync(_fixture.ConnectionString, accountId);

            int after = await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId);
            Assert.Equal(0, after);
        }

        private async Task<TelemetryEventDto> GetSingleEventAsync(string accountId, string type)
        {
            List<TelemetryEventDto> events = await GetEventsAsync(accountId, type);
            return Assert.Single(events);
        }

        private async Task<List<TelemetryEventDto>> GetEventsAsync(string accountId, string type)
        {
            (_, string adminToken) = await _fixture.Client.CreateAdminAsync("query-admin-" + Guid.NewGuid());
            var response = await _fixture.Client.AdminGet("/admin/telemetry/events?type=" + type + "&limit=5000", adminToken);
            response.EnsureSuccessStatusCode();
            TelemetryEventsResponse body = await response.Content.ReadFromJsonAsync<TelemetryEventsResponse>(Json.Options);
            return body.Items.Where(e => e.AccountId == accountId).ToList();
        }

        private static T Deserialize<T>(JsonElement element)
        {
            return element.Deserialize<T>(Json.Options);
        }
    }
}
