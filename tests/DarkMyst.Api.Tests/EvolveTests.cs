using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Evolve;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// docs/03-evolve-spec.md's five mandatory server steps, exercised end-to-end against a live
    /// database — including the two concurrency/atomicity cases the acceptance criterion cares
    /// about most (docs/06-roadmap.md phase D: "เน็ตหลุดหรือคำขอซ้ำไม่ทำของหาย/เพิ่มซ้ำ").
    /// </summary>
    public sealed class EvolveTests : IClassFixture<ApiTestFixture>
    {
        private const string SubjectCharacterId = "chr_ashen_knight_i"; // requires level 20, 2 fodder, 5000g, 3 mat_ashen_sigil
        private const string MaterialId = "mat_ashen_sigil";

        private readonly ApiTestFixture _fixture;

        public EvolveTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Preview_then_confirm_produce_matching_stats_and_spend_exactly_the_previewed_cost()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter fodder1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter fodder2 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            await Seed.GrantGoldAsync(client, token, 10_000);
            await Seed.GrantMaterialAsync(client, token, MaterialId, 5);

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { fodder1.InstanceId, fodder2.InstanceId },
                focus = "Offense"
            };

            var previewResponse = await client.ApiPost("/evolve/preview", token, body: request);
            previewResponse.EnsureSuccessStatusCode();
            EvolvePreviewResponse preview = await previewResponse.Content.ReadFromJsonAsync<EvolvePreviewResponse>(Json.Options);
            Assert.True(preview.CanEvolve, string.Join("; ", preview.Blockers));

            string key = "evolve-" + subject.InstanceId;
            var confirmResponse = await client.ApiPost("/evolve/confirm", token, key, request);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
            EvolvedCharacterResponse result = await confirmResponse.Content.ReadFromJsonAsync<EvolvedCharacterResponse>(Json.Options);

            // The preview is what the player confirmed against — it must be exactly what happened,
            // never an approximation (docs/03-evolve-spec.md: "EvolvePreview.StatsAfter ต้องเท่ากับ
            // ผลจริงเสมอ").
            Assert.Equal(preview.ResultCharacterId, result.CharacterId);
            Assert.Equal(preview.StatsAfter, result.StatsAfter);
            Assert.Equal(preview.GoldCost, result.GoldSpent);
            Assert.Equal(5000, result.GoldSpent);
            Assert.Equal(3, result.MaterialsSpent[MaterialId]);

            var meResponse = await client.ApiGet("/accounts/me", token);
            var me = await meResponse.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);
            Assert.Equal(10_000 - 5000, me.Gold);
        }

        [Fact]
        public async Task Replaying_the_same_confirm_request_does_not_spend_twice()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter f1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter f2 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            await Seed.GrantGoldAsync(client, token, 10_000);
            await Seed.GrantMaterialAsync(client, token, MaterialId, 5);

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { f1.InstanceId, f2.InstanceId },
                focus = "Guard"
            };
            const string key = "replayed-evolve-key";

            var first = await client.ApiPost("/evolve/confirm", token, key, request);
            var second = await client.ApiPost("/evolve/confirm", token, key, request);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());

            var meResponse = await client.ApiGet("/accounts/me", token);
            var me = await meResponse.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);
            // Spent once, not twice: 10_000 - 5000, never 10_000 - 10_000.
            Assert.Equal(5000, me.Gold);
        }

        [Fact]
        public async Task Two_concurrent_evolves_that_both_want_the_same_fodder_leave_exactly_one_winner()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            OwnedCharacter subjectA = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter subjectB = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter f1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter contested = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter f3 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            await Seed.GrantGoldAsync(client, token, 20_000);
            await Seed.GrantMaterialAsync(client, token, MaterialId, 10);

            var requestA = new
            {
                subjectInstanceId = subjectA.InstanceId,
                fodderInstanceIds = new[] { f1.InstanceId, contested.InstanceId },
                focus = (string)null
            };
            var requestB = new
            {
                subjectInstanceId = subjectB.InstanceId,
                fodderInstanceIds = new[] { contested.InstanceId, f3.InstanceId },
                focus = (string)null
            };

            Task<System.Net.Http.HttpResponseMessage> taskA = client.ApiPost("/evolve/confirm", token, "concurrent-a", requestA);
            Task<System.Net.Http.HttpResponseMessage> taskB = client.ApiPost("/evolve/confirm", token, "concurrent-b", requestB);
            await Task.WhenAll(taskA, taskB);

            bool aOk = taskA.Result.StatusCode == HttpStatusCode.OK;
            bool bOk = taskB.Result.StatusCode == HttpStatusCode.OK;

            // Exactly one wins. The other must fail cleanly (a 409 refusal, never a 500 and never
            // a second success) — that is the whole point of locking the fodder rows before
            // re-checking Evolution.Preview inside the transaction (EvolveService.EvolveAsync).
            Assert.True(aOk ^ bOk, "Exactly one of the two concurrent evolves should have succeeded.");

            var loserResponse = aOk ? taskB.Result : taskA.Result;
            Assert.Equal(HttpStatusCode.Conflict, loserResponse.StatusCode);

            // The fodder was not consumed twice: exactly one of the two subjects evolved.
            OwnedCharacter subjectAAfter = await GetCharacterAsync(client, token, subjectA.InstanceId);
            OwnedCharacter subjectBAfter = await GetCharacterAsync(client, token, subjectB.InstanceId);
            int evolvedCount = new[] { subjectAAfter, subjectBAfter }.Count(c => c.CharacterId != SubjectCharacterId);
            Assert.Equal(1, evolvedCount);

            // Only one evolve's gold was actually spent — the loser's transaction rolled back
            // completely (docs/06-roadmap.md acceptance criterion: no partial spend).
            var meResponse = await client.ApiGet("/accounts/me", token);
            var me = await meResponse.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);
            Assert.Equal(20_000 - 5000, me.Gold);
        }

        [Fact]
        public async Task Placing_offered_fodder_into_a_team_after_preview_is_refused_at_confirm()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter f1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter f2 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            // A fifth character so the saved team below does not need to reuse the subject/fodder.
            OwnedCharacter filler1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 1);
            OwnedCharacter filler2 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 1);
            OwnedCharacter filler3 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 1);
            await Seed.GrantGoldAsync(client, token, 10_000);
            await Seed.GrantMaterialAsync(client, token, MaterialId, 5);

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { f1.InstanceId, f2.InstanceId },
                focus = (string)null
            };

            var previewResponse = await client.ApiPost("/evolve/preview", token, body: request);
            EvolvePreviewResponse preview = await previewResponse.Content.ReadFromJsonAsync<EvolvePreviewResponse>(Json.Options);
            Assert.True(preview.CanEvolve);

            // f2 gets placed into a saved team between preview and confirm.
            var teamRequest = new
            {
                name = "sneaky",
                leaderSlot = 0,
                placements = new object[]
                {
                    new { slot = 0, instanceId = f2.InstanceId },
                    new { slot = 1, instanceId = filler1.InstanceId },
                    new { slot = 2, instanceId = filler2.InstanceId },
                    new { slot = 3, instanceId = filler3.InstanceId }
                }
            };
            var teamResponse = await client.ApiPost("/teams", token, "team-" + f2.InstanceId, teamRequest);
            teamResponse.EnsureSuccessStatusCode();

            var confirmResponse = await client.ApiPost("/evolve/confirm", token, "confirm-after-team-" + subject.InstanceId, request);
            Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
            var body = await confirmResponse.Content.ReadFromJsonAsync<RefusalDto>(Json.Options);
            Assert.Contains(body.Blockers, b => b.Contains("team or an expedition"));

            // Nothing was spent — refusal must leave the account exactly as it was.
            var meResponse = await client.ApiGet("/accounts/me", token);
            var me = await meResponse.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);
            Assert.Equal(10_000, me.Gold);
        }

        [Fact]
        public async Task Insufficient_gold_is_refused_and_spends_nothing()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 20);
            OwnedCharacter f1 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            OwnedCharacter f2 = await Seed.GrantCharacterAsync(client, token, SubjectCharacterId, level: 5);
            await Seed.GrantMaterialAsync(client, token, MaterialId, 5);
            // Deliberately no gold granted — the account starts at 0.

            var request = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { f1.InstanceId, f2.InstanceId },
                focus = (string)null
            };

            var confirmResponse = await client.ApiPost("/evolve/confirm", token, "poor-" + subject.InstanceId, request);
            Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

            OwnedCharacter subjectAfter = await GetCharacterAsync(client, token, subject.InstanceId);
            Assert.Equal(SubjectCharacterId, subjectAfter.CharacterId); // unchanged

            OwnedCharacter f1After = await GetCharacterAsync(client, token, f1.InstanceId);
            Assert.NotNull(f1After); // fodder was not consumed
        }

        private static async Task<OwnedCharacter> GetCharacterAsync(System.Net.Http.HttpClient client, string token, string instanceId)
        {
            var response = await client.ApiGet("/debug/character/" + instanceId, token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OwnedCharacter>(Json.Options);
        }

        private sealed record AccountMeDto(string AccountId, string Kind, int Gold, object Summary);

        private sealed record RefusalDto(string Error, List<string> Blockers);
    }
}
