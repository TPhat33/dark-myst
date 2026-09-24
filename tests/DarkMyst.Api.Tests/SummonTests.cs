using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Summon;
using DarkMyst.Api.Telemetry;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Combat;
using DarkMyst.Content;
using DarkMyst.Sim;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// docs/12-summon-spec.md's pull/spark/attune endpoints, exercised end to end against a live
    /// database. Predicted outcomes for scripted seeds are computed by calling
    /// <see cref="SummonEngine.ResolvePull"/> directly in the test (the exact function the server
    /// calls, via <see cref="DarkMyst.Api.Summon.SummonService"/>) rather than hardcoding numbers a
    /// content edit could silently invalidate.
    /// <para>
    /// <see cref="SummonTestHooks.SeedOverride"/> is set/cleared per test in a
    /// try/finally — see that type's own remarks on why production code never reads it.
    /// </para>
    /// </summary>
    public sealed class SummonTests : IClassFixture<ApiTestFixture>
    {
        private const int PricePerPull = SummonService.PullPriceGemsPerPull;

        private static readonly Lazy<ContentPack> Pack = new Lazy<ContentPack>(
            () => ContentPack.LoadFromDirectory(ContentLocator.Locate(new ConfigurationBuilder().Build())));

        private static readonly Lazy<Dictionary<int, List<CharacterData>>> Pool = new Lazy<Dictionary<int, List<CharacterData>>>(
            () => SummonEngine.BuildPool(Pack.Value));

        private readonly ApiTestFixture _fixture;

        public SummonTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Pull_matches_the_shared_engine_for_a_scripted_seed_and_updates_pity_floor_and_spark()
        {
            const ulong seed = 54321;
            List<SummonPullOutcome> expected = Predict(seed, pullsSincePity: 0, pullsSinceFloor: 0, count: 10);

            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 10);

            using (WithSeed(seed))
            {
                var response = await client.ApiPost("/summon/pull", token, "pull-" + accountId, new { count = 10 });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                SummonPullResponse body = await response.Content.ReadFromJsonAsync<SummonPullResponse>(Json.Options);

                Assert.Equal(10, body.Pulls.Count);

                var seenLines = new HashSet<string>(StringComparer.Ordinal);
                int expectedNewGrants = 0;
                for (int i = 0; i < 10; i++)
                {
                    SummonPullOutcome want = expected[i];
                    SummonPullResultDto got = body.Pulls[i];
                    Assert.Equal(want.Character.Id, got.CharacterId);
                    Assert.Equal(want.Character.LineId, got.LineId);
                    Assert.Equal(want.ActualTier, got.Rarity);

                    bool wantDuplicate = !seenLines.Add(want.Character.LineId);
                    Assert.Equal(wantDuplicate, got.IsDuplicate);
                    if (wantDuplicate)
                    {
                        Assert.Equal(SummonRules.Proposed.DuplicateShardsForRarity(want.ActualTier), got.ShardsGranted);
                        Assert.Null(got.InstanceId);
                    }
                    else
                    {
                        Assert.Equal(0, got.ShardsGranted);
                        Assert.NotNull(got.InstanceId);
                        expectedNewGrants++;
                    }
                }

                SummonPullOutcome last = expected[9];
                Assert.Equal(last.PullsSincePityAfter, body.PullsSinceLastPity);
                Assert.Equal(last.PullsSinceFloorAfter, body.PullsSinceLastFloor);
                Assert.Equal(10, body.SparkPoints);
                Assert.Equal(PricePerPull * 10, body.GemsSpent);

                // Ground truth: exactly one owned_characters row per first copy, and exactly the
                // right Echo shard balance per line — read back independent of the response DTO.
                var stateResponse = await client.ApiGet("/summon/state", token);
                stateResponse.EnsureSuccessStatusCode();
                SummonStateResponse state = await stateResponse.Content.ReadFromJsonAsync<SummonStateResponse>(Json.Options);
                Assert.Equal(body.PullsSinceLastPity, state.PullsSinceLastPity);
                Assert.Equal(body.PullsSinceLastFloor, state.PullsSinceLastFloor);
                Assert.Equal(body.SparkPoints, state.SparkPoints);

                var shardsByLine = state.EchoShards.ToDictionary(s => s.LineId, s => s.ShardCount);
                var expectedShardsByLine = new Dictionary<string, int>(StringComparer.Ordinal);
                var ownedSoFar = new HashSet<string>(StringComparer.Ordinal);
                foreach (SummonPullOutcome outcome in expected)
                {
                    if (!ownedSoFar.Add(outcome.Character.LineId))
                    {
                        expectedShardsByLine.TryGetValue(outcome.Character.LineId, out int soFar);
                        expectedShardsByLine[outcome.Character.LineId] =
                            soFar + SummonRules.Proposed.DuplicateShardsForRarity(outcome.ActualTier);
                    }
                }

                foreach (KeyValuePair<string, int> want in expectedShardsByLine)
                {
                    Assert.True(shardsByLine.TryGetValue(want.Key, out int got), "Missing shard balance for " + want.Key);
                    Assert.Equal(want.Value, got);
                }

                // Telemetry: one summon_pulled per pull, one character_obtained per first copy.
                Assert.Equal(10, await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.SummonPulled));
                Assert.Equal(expectedNewGrants,
                    await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.CharacterObtained));
            }
        }

        [Fact]
        public async Task Pull_duplicate_grants_shards_not_a_second_character()
        {
            // seed=1 at pity=0/floor=0 resolves to an R2 line (chr_mire_hexer_i) — reusing the same
            // seed for a second, independent single pull from a fresh account reproduces the exact
            // same RNG draws (pity/floor bookkeeping stays in the flat, pre-soft-pity, pre-floor
            // region for both calls — see SummonEngine's remarks), so it deterministically lands on
            // the same line again.
            const ulong seed = 1;
            SummonPullOutcome want = Predict(seed, 0, 0, 1)[0];

            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 2);

            using (WithSeed(seed))
            {
                var first = await client.ApiPost("/summon/pull", token, "dup-first-" + accountId, new { count = 1 });
                first.EnsureSuccessStatusCode();
                SummonPullResponse firstBody = await first.Content.ReadFromJsonAsync<SummonPullResponse>(Json.Options);
                Assert.False(firstBody.Pulls[0].IsDuplicate);
                Assert.NotNull(firstBody.Pulls[0].InstanceId);
                Assert.Equal(want.Character.Id, firstBody.Pulls[0].CharacterId);

                var second = await client.ApiPost("/summon/pull", token, "dup-second-" + accountId, new { count = 1 });
                second.EnsureSuccessStatusCode();
                SummonPullResponse secondBody = await second.Content.ReadFromJsonAsync<SummonPullResponse>(Json.Options);
                Assert.True(secondBody.Pulls[0].IsDuplicate);
                Assert.Null(secondBody.Pulls[0].InstanceId);
                Assert.Equal(want.Character.Id, secondBody.Pulls[0].CharacterId);
                Assert.Equal(SummonRules.Proposed.DuplicateShardsForRarity(want.ActualTier), secondBody.Pulls[0].ShardsGranted);
            }

            // Exactly one owned_characters row for this line, not two.
            var stateResponse = await client.ApiGet("/summon/state", token);
            SummonStateResponse state = await stateResponse.Content.ReadFromJsonAsync<SummonStateResponse>(Json.Options);
            EchoShardBalanceDto shard = Assert.Single(state.EchoShards);
            Assert.Equal(want.Character.LineId, shard.LineId);
            Assert.Equal(SummonRules.Proposed.DuplicateShardsForRarity(want.ActualTier), shard.ShardCount);
        }

        [Fact]
        public async Task Replaying_the_same_pull_key_does_not_double_grant_or_double_charge()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 5);
            const string key = "replayed-pull-key";

            var first = await client.ApiPost("/summon/pull", token, key, new { count = 1 });
            var second = await client.ApiPost("/summon/pull", token, key, new { count = 1 });

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());

            int gems = await RawDb.GetGemsAsync(_fixture.ConnectionString, accountId);
            Assert.Equal(PricePerPull * 5 - PricePerPull, gems);
            Assert.Equal(1, await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.SummonPulled));
        }

        [Fact]
        public async Task Two_concurrent_identical_pull_requests_grant_and_charge_exactly_once()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 5);
            const string key = "concurrent-pull-key";

            Task<System.Net.Http.HttpResponseMessage> t1 = client.ApiPost("/summon/pull", token, key, new { count = 1 });
            Task<System.Net.Http.HttpResponseMessage> t2 = client.ApiPost("/summon/pull", token, key, new { count = 1 });
            var responses = await Task.WhenAll(t1, t2);

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            string bodyA = await responses[0].Content.ReadAsStringAsync();
            string bodyB = await responses[1].Content.ReadAsStringAsync();
            Assert.Equal(bodyA, bodyB);

            int gems = await RawDb.GetGemsAsync(_fixture.ConnectionString, accountId);
            Assert.Equal(PricePerPull * 5 - PricePerPull, gems);
            Assert.Equal(1, await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.SummonPulled));
        }

        [Fact]
        public async Task Insufficient_gems_refuses_cleanly_with_no_partial_state()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull - 1);

            var response = await client.ApiPost("/summon/pull", token, "broke-" + accountId, new { count = 1 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            int gems = await RawDb.GetGemsAsync(_fixture.ConnectionString, accountId);
            Assert.Equal(PricePerPull - 1, gems);
            Assert.Equal(0, await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.SummonPulled));
            Assert.Equal(0, await RawDb.GetTelemetryEventCountAsync(_fixture.ConnectionString, accountId, TelemetryEventTypes.CharacterObtained));
        }

        [Fact]
        public async Task Invalid_pull_count_is_refused()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 10);

            var response = await client.ApiPost("/summon/pull", token, "bad-count", new { count = 3 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Spark_redeem_refuses_under_threshold()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await RawDb.SetSparkPointsAsync(_fixture.ConnectionString, accountId, SummonRules.Proposed.SparkThreshold - 1);

            var response = await client.ApiPost("/summon/spark-redeem", token, "spark-" + accountId, new { lineId = "line_ashen_knight" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Spark_redeem_succeeds_at_threshold_and_carries_over_leftover()
        {
            const int leftover = 7;
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await RawDb.SetSparkPointsAsync(_fixture.ConnectionString, accountId, SummonRules.Proposed.SparkThreshold + leftover);

            var response = await client.ApiPost(
                "/summon/spark-redeem", token, "spark-ok-" + accountId, new { lineId = "line_ashen_knight" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SummonSparkRedeemResponse body = await response.Content.ReadFromJsonAsync<SummonSparkRedeemResponse>(Json.Options);

            Assert.False(body.IsDuplicate);
            Assert.NotNull(body.InstanceId);
            Assert.Equal("chr_ashen_knight_i", body.CharacterId);
            Assert.Equal(leftover, body.SparkPointsRemaining);

            var stateResponse = await client.ApiGet("/summon/state", token);
            SummonStateResponse state = await stateResponse.Content.ReadFromJsonAsync<SummonStateResponse>(Json.Options);
            Assert.Equal(leftover, state.SparkPoints);
            Assert.False(state.CanRedeemSpark);

            Assert.True(await RawDb.CharacterExistsAsync(_fixture.ConnectionString, body.InstanceId));
        }

        [Fact]
        public async Task Spark_redeem_of_an_already_owned_line_grants_shards_not_a_second_character()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter existing = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            await RawDb.SetSparkPointsAsync(_fixture.ConnectionString, accountId, SummonRules.Proposed.SparkThreshold);

            // Debug grants deliberately do not populate account_lines (see AccountLineEntity's
            // remarks) — plant the "already owns this line" fact for real so this test exercises
            // the actual duplicate path, not a debug-only quirk.
            await SeedAccountLineAsync(accountId, "line_ashen_knight");

            var response = await client.ApiPost(
                "/summon/spark-redeem", token, "spark-dup-" + accountId, new { lineId = "line_ashen_knight" });
            response.EnsureSuccessStatusCode();
            SummonSparkRedeemResponse body = await response.Content.ReadFromJsonAsync<SummonSparkRedeemResponse>(Json.Options);

            Assert.True(body.IsDuplicate);
            Assert.Null(body.InstanceId);
            Assert.Equal(SummonRules.Proposed.DuplicateShardsForRarity(3), body.ShardsGranted);
            Assert.NotNull(existing.InstanceId);
        }

        [Fact]
        public async Task Spark_redeem_of_an_unknown_line_is_refused()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await RawDb.SetSparkPointsAsync(_fixture.ConnectionString, accountId, SummonRules.Proposed.SparkThreshold);

            var response = await client.ApiPost(
                "/summon/spark-redeem", token, "spark-bad-" + accountId, new { lineId = "not_a_real_line" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Attune_refuses_for_an_instance_the_caller_does_not_own()
        {
            var client = _fixture.Client;
            (string _, string ownerToken) = await client.CreateGuestAsync();
            (string _, string otherToken) = await client.CreateGuestAsync();
            OwnedCharacter owned = await Seed.GrantCharacterAsync(client, ownerToken, "chr_ashen_knight_i", level: 1);

            var response = await client.ApiPost("/summon/attune", otherToken, "attune-notowned",
                new { instanceId = owned.InstanceId, shardsToSpend = 4 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Attune_refuses_for_an_unknown_instance()
        {
            var client = _fixture.Client;
            (string _, string token) = await client.CreateGuestAsync();

            var response = await client.ApiPost("/summon/attune", token, "attune-unknown",
                new { instanceId = "does-not-exist", shardsToSpend = 4 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Attune_refuses_for_a_character_that_is_not_stage_one()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            // chr_ashen_knight_ii is stage 2 of line_ashen_knight — Echo shards are a stage-I
            // concept (docs/12 "สุ่มได้เฉพาะร่างขั้น 1 เท่านั้น").
            OwnedCharacter stage2 = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_ii", level: 20);
            await RawDb.SetShardCountAsync(_fixture.ConnectionString, accountId, "line_ashen_knight", 100);

            var response = await client.ApiPost("/summon/attune", token, "attune-stage2",
                new { instanceId = stage2.InstanceId, shardsToSpend = 4 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Attune_refuses_when_shardsToSpend_exceeds_available()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter stage1 = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            await RawDb.SetShardCountAsync(_fixture.ConnectionString, accountId, "line_ashen_knight", 10);

            var response = await client.ApiPost("/summon/attune", token, "attune-toomuch",
                new { instanceId = stage1.InstanceId, shardsToSpend = 11 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Attune_raises_inherited_bonus_and_banks_leftover_shards_under_the_per_mille_rate()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter stage1 = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            await RawDb.SetShardCountAsync(_fixture.ConnectionString, accountId, "line_ashen_knight", 22);

            var response = await client.ApiPost("/summon/attune", token, "attune-ok",
                new { instanceId = stage1.InstanceId, shardsToSpend = 22 });
            response.EnsureSuccessStatusCode();
            SummonAttuneResponse body = await response.Content.ReadFromJsonAsync<SummonAttuneResponse>(Json.Options);

            // 22 shards / 4 per-mille = 5 per-mille, using 20 shards; 2 shards stay banked.
            Assert.Equal(0, body.InheritedBonusPerMilleBefore);
            Assert.Equal(5, body.InheritedBonusPerMilleAfter);
            Assert.Equal(20, body.ShardsSpent);
            Assert.Equal(2, body.ShardsBanked);

            var stateResponse = await client.ApiGet("/summon/state", token);
            SummonStateResponse state = await stateResponse.Content.ReadFromJsonAsync<SummonStateResponse>(Json.Options);
            EchoShardBalanceDto shard = Assert.Single(state.EchoShards);
            Assert.Equal(2, shard.ShardCount);

            Assert.Equal(1, await RawDb.GetTelemetryEventCountAsync(
                _fixture.ConnectionString, accountId, TelemetryEventTypes.AttuneCompleted));
        }

        [Fact]
        public async Task Attune_caps_at_300_per_mille_even_with_more_shards_than_needed()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            OwnedCharacter stage1 = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 1);
            // 1203 shards: 300 per-mille needs exactly 1200 (the cap); 3 stay banked regardless of
            // how many more than that were offered.
            await RawDb.SetShardCountAsync(_fixture.ConnectionString, accountId, "line_ashen_knight", 1203);

            var response = await client.ApiPost("/summon/attune", token, "attune-cap",
                new { instanceId = stage1.InstanceId, shardsToSpend = 1203 });
            response.EnsureSuccessStatusCode();
            SummonAttuneResponse body = await response.Content.ReadFromJsonAsync<SummonAttuneResponse>(Json.Options);

            Assert.Equal(300, body.InheritedBonusPerMilleAfter);
            Assert.Equal(1200, body.ShardsSpent);
            Assert.Equal(3, body.ShardsBanked);

            // A second attempt to spend the 3 banked shards changes nothing further — already
            // at cap, so nothing more is ever consumed no matter how the player asks.
            var second = await client.ApiPost("/summon/attune", token, "attune-cap-again",
                new { instanceId = stage1.InstanceId, shardsToSpend = 3 });
            second.EnsureSuccessStatusCode();
            SummonAttuneResponse secondBody = await second.Content.ReadFromJsonAsync<SummonAttuneResponse>(Json.Options);
            Assert.Equal(300, secondBody.InheritedBonusPerMilleAfter);
            Assert.Equal(0, secondBody.ShardsSpent);
            Assert.Equal(3, secondBody.ShardsBanked);
        }

        [Fact]
        public async Task GetState_reflects_pity_floor_spark_and_shards_after_a_sequence_of_operations()
        {
            const ulong seed = 54321;

            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();
            await Seed.GrantGemsAsync(client, token, PricePerPull * 4);

            // /summon/pull only allows count 1 or 10 — issue four single pulls in a row, each
            // reseeded to the same fixed seed (every call starts a fresh DeterministicRandom, same
            // as SummonService.PullAsync does per request), while independently predicting each
            // call's outcome the same way: a fresh DeterministicRandom(seed) each time, fed the
            // pity/floor state carried over from the previous predicted call — exactly mirroring
            // what the account's persisted summon_state does server-side.
            int pity = 0;
            int floor = 0;
            for (int i = 0; i < 4; i++)
            {
                var predictRng = new DeterministicRandom(seed);
                SummonPullOutcome predicted = SummonEngine.ResolvePull(SummonRules.Proposed, Pool.Value, pity, floor, predictRng);
                pity = predicted.PullsSincePityAfter;
                floor = predicted.PullsSinceFloorAfter;

                using (WithSeed(seed))
                {
                    var response = await client.ApiPost("/summon/pull", token, "state-seq-" + accountId + "-" + i, new { count = 1 });
                    response.EnsureSuccessStatusCode();
                }
            }

            var stateResponse = await client.ApiGet("/summon/state", token);
            stateResponse.EnsureSuccessStatusCode();
            SummonStateResponse state = await stateResponse.Content.ReadFromJsonAsync<SummonStateResponse>(Json.Options);

            Assert.Equal(pity, state.PullsSinceLastPity);
            Assert.Equal(floor, state.PullsSinceLastFloor);
            Assert.Equal(4, state.SparkPoints);
            Assert.Equal(SummonRules.Proposed.SparkThreshold, state.SparkThreshold);
            Assert.False(state.CanRedeemSpark);
        }

        // ------------------------------------------------------------------

        private static List<SummonPullOutcome> Predict(ulong seed, int pullsSincePity, int pullsSinceFloor, int count)
        {
            var rng = new DeterministicRandom(seed);
            var results = new List<SummonPullOutcome>(count);
            int pity = pullsSincePity;
            int floor = pullsSinceFloor;
            for (int i = 0; i < count; i++)
            {
                SummonPullOutcome outcome = SummonEngine.ResolvePull(SummonRules.Proposed, Pool.Value, pity, floor, rng);
                results.Add(outcome);
                pity = outcome.PullsSincePityAfter;
                floor = outcome.PullsSinceFloorAfter;
            }

            return results;
        }

        private static IDisposable WithSeed(ulong seed)
        {
            SummonTestHooks.SeedOverride = () => seed;
            return new SeedScope();
        }

        private sealed class SeedScope : IDisposable
        {
            public void Dispose()
            {
                SummonTestHooks.SeedOverride = null;
            }
        }

        private async Task SeedAccountLineAsync(string accountId, string lineId)
        {
            await using var connection = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                @"INSERT INTO account_lines (account_id, line_id, first_obtained_at)
                  VALUES (@id, @line, now()) ON CONFLICT DO NOTHING",
                connection);
            cmd.Parameters.AddWithValue("id", accountId);
            cmd.Parameters.AddWithValue("line", lineId);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
