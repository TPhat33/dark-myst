using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Tests.Infra;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// The idempotency contract itself (server/DarkMyst.Api/Idempotency/IdempotencyService.cs),
    /// tested independently of any particular business endpoint — using /teams (a small, cheap
    /// mutation) so these assertions are about the shared mechanism, not evolve's own rules.
    /// </summary>
    public sealed class IdempotencyTests : IClassFixture<ApiTestFixture>
    {
        private readonly ApiTestFixture _fixture;

        public IdempotencyTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Sequential_replay_with_the_same_key_returns_the_stored_response_without_running_twice()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            await Seed.GrantGoldAsync(client, token, 1);

            var request = new { amount = 250 };
            const string key = "sequential-replay";

            var first = await client.ApiPost("/debug/grant-gold", token, key, request);
            var second = await client.ApiPost("/debug/grant-gold", token, key, request);

            first.EnsureSuccessStatusCode();
            second.EnsureSuccessStatusCode();
            Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Two_simultaneous_identical_requests_with_the_same_key_mutate_exactly_once()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            // /debug/grant-gold is now also routed through IdempotencyService (see
            // Debug_grant_gold_replay_with_the_same_key_grants_exactly_once below), but this test
            // targets /teams instead, precisely to prove the *shared* mechanism generically rather
            // than re-prove it against the one endpoint the bug was found on: two requests issued
            // together with Task.WhenAll, not one after the other.
            var characterA = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var characterB = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var characterC = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var characterD = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);

            var request = new
            {
                name = "concurrent-team",
                leaderSlot = 0,
                placements = new object[]
                {
                    new { slot = 0, instanceId = characterA.InstanceId },
                    new { slot = 1, instanceId = characterB.InstanceId },
                    new { slot = 2, instanceId = characterC.InstanceId },
                    new { slot = 3, instanceId = characterD.InstanceId }
                }
            };
            const string key = "truly-concurrent-team-create";

            Task<System.Net.Http.HttpResponseMessage> t1 = client.ApiPost("/teams", token, key, request);
            Task<System.Net.Http.HttpResponseMessage> t2 = client.ApiPost("/teams", token, key, request);
            await Task.WhenAll(t1, t2);

            t1.Result.EnsureSuccessStatusCode();
            t2.Result.EnsureSuccessStatusCode();

            var body1 = await t1.Result.Content.ReadFromJsonAsync<TeamDto>(Json.Options);
            var body2 = await t2.Result.Content.ReadFromJsonAsync<TeamDto>(Json.Options);

            // Both responses describe the very same created team, not two different ones — proving
            // only one insert ever happened even though both requests ran at the same time.
            Assert.Equal(body1.Id, body2.Id);

            // A racing insert that lost still leaves each character placed exactly once: if the
            // operation had run twice, this would have thrown a duplicate-membership error instead.
            var characterAAfter = await GetCharacterAsync(client, token, characterA.InstanceId);
            Assert.True(characterAAfter.IsInUse);
        }

        [Fact]
        public async Task Reusing_a_key_with_a_different_body_is_refused()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            var a = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var b = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var c = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            var d = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);

            const string key = "reused-with-different-body";
            var first = await client.ApiPost("/teams", token, key, new
            {
                name = "team-one",
                leaderSlot = 0,
                placements = new object[] { new { slot = 0, instanceId = a.InstanceId } }
            });
            first.EnsureSuccessStatusCode();

            var second = await client.ApiPost("/teams", token, key, new
            {
                name = "team-two-different-body",
                leaderSlot = 1,
                placements = new object[]
                {
                    new { slot = 1, instanceId = b.InstanceId },
                    new { slot = 2, instanceId = c.InstanceId },
                    new { slot = 3, instanceId = d.InstanceId }
                }
            });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task Debug_grant_gold_replay_with_the_same_key_grants_exactly_once()
        {
            // Regression test for the bug the review round found: /debug/grant-gold (and its two
            // siblings) used to accept an Idempotency-Key header and silently ignore it, so a
            // replayed request granted gold a second time instead of returning the stored result.
            // This reproduces exactly the curl sequence that proved it.
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            var grant = new { amount = 50000 };
            var first = await client.ApiPost("/debug/grant-gold", token, "K1", grant);
            var replay = await client.ApiPost("/debug/grant-gold", token, "K1", grant);
            first.EnsureSuccessStatusCode();
            replay.EnsureSuccessStatusCode();

            // The replay must return the exact stored result from the first call, not run the
            // grant again — this is the same assertion Sequential_replay_with_the_same_key... uses.
            Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());

            var second = await client.ApiPost("/debug/grant-gold", token, "K2", new { amount = 7 });
            second.EnsureSuccessStatusCode();

            var me = await client.ApiGet("/accounts/me", token);
            me.EnsureSuccessStatusCode();
            var account = await me.Content.ReadFromJsonAsync<AccountMeDto>(Json.Options);

            // 50000 (K1, granted once) + 7 (K2) = 50007 — not 100007, which is what the bug produced
            // by granting the K1 amount a second time on replay.
            Assert.Equal(50007, account.Gold);
        }

        private sealed record AccountMeDto(string AccountId, string Kind, int Gold);

        private static async Task<DarkMyst.Content.OwnedCharacter> GetCharacterAsync(
            System.Net.Http.HttpClient client, string token, string instanceId)
        {
            var response = await client.ApiGet("/debug/character/" + instanceId, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DarkMyst.Content.OwnedCharacter>(Json.Options);
        }

        private sealed record TeamDto(string Id, string Name, int LeaderSlot, List<object> Placements);
    }
}
