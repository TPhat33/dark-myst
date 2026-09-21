using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// docs/07-testing-plan.md's "เศรษฐกิจ" row: summing the ledger reproduces current inventory
    /// exactly, for every account, after any sequence of operations. This test runs a mixed
    /// sequence — grants, an evolve, and an expedition — then re-derives every balance from
    /// ledger_entries via raw SQL (server/DarkMyst.Api/Ledger/LedgerService.cs never gets asked;
    /// this checks its output, not itself) and compares.
    /// </summary>
    public sealed class LedgerReconciliationTests : IClassFixture<ApiTestFixture>
    {
        private readonly ApiTestFixture _fixture;

        public LedgerReconciliationTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Ledger_sums_reproduce_gold_materials_and_character_existence_after_a_mixed_sequence()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            await Seed.GrantGoldAsync(client, token, 50_000);
            await Seed.GrantMaterialAsync(client, token, "mat_ashen_sigil", 20);

            OwnedCharacter subject = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 20);
            OwnedCharacter fodderA = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 5);
            OwnedCharacter fodderB = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 5);
            OwnedCharacter expeditionLeader = await Seed.GrantCharacterAsync(client, token, "chr_ashen_knight_i", level: 20);

            var evolveRequest = new
            {
                subjectInstanceId = subject.InstanceId,
                fodderInstanceIds = new[] { fodderA.InstanceId, fodderB.InstanceId },
                focus = (string)null
            };
            var evolveResponse = await client.ApiPost("/evolve/confirm", token, "reconcile-evolve", evolveRequest);
            Assert.Equal(HttpStatusCode.OK, evolveResponse.StatusCode);

            var startRequest = new
            {
                stageId = "stg_ashfields",
                leaderSlot = 0,
                placements = new[] { new { slot = 0, instanceId = expeditionLeader.InstanceId } },
                seed = 99UL
            };
            var startResponse = await client.ApiPost("/expeditions/start", token, "reconcile-start", startRequest);
            startResponse.EnsureSuccessStatusCode();
            ExpeditionRunSummary summary = await startResponse.Content.ReadFromJsonAsync<ExpeditionRunSummary>(Json.Options);

            await client.ApiPost("/expeditions/" + summary.RunId + "/choose", token, "reconcile-choose", new { choiceIndex = 0 });
            // The node resolved above may already have ended the run (a lost battle ends it
            // immediately); abandoning an already-ended run is correctly refused — either outcome
            // is fine here, only the ledger's own internal consistency is under test.
            await client.ApiPost("/expeditions/" + summary.RunId + "/abandon", token, "reconcile-abandon");

            // ---- Gold ----
            int actualGold = await RawDb.GetGoldAsync(_fixture.ConnectionString, accountId);
            int ledgerGold = await RawDb.GetLedgerGoldSumAsync(_fixture.ConnectionString, accountId);
            Assert.Equal(actualGold, ledgerGold);

            // ---- Materials ----
            Dictionary<string, int> actualMaterials = await RawDb.GetMaterialAmountsAsync(_fixture.ConnectionString, accountId);
            Dictionary<string, int> ledgerMaterials = await RawDb.GetLedgerMaterialSumsAsync(_fixture.ConnectionString, accountId);
            foreach (KeyValuePair<string, int> pair in ledgerMaterials)
            {
                Assert.True(actualMaterials.TryGetValue(pair.Key, out int actualAmount), "Missing material row for " + pair.Key);
                Assert.Equal(pair.Value, actualAmount);
            }

            // ---- Characters: every ref_id's net ledger delta must match whether it still exists ----
            Dictionary<string, int> characterNet = await RawDb.GetLedgerCharacterNetAsync(_fixture.ConnectionString, accountId);
            foreach (KeyValuePair<string, int> pair in characterNet)
            {
                bool exists = await RawDb.CharacterExistsAsync(_fixture.ConnectionString, pair.Key);
                if (pair.Value > 0)
                {
                    Assert.True(exists, "Character " + pair.Key + " has a net positive ledger balance but no row.");
                }
                else
                {
                    Assert.False(exists, "Character " + pair.Key + " has a net-zero ledger balance but still has a row.");
                }
            }

            // fodderA/fodderB were consumed by evolve: net ledger delta 0, row gone.
            Assert.Equal(0, characterNet[fodderA.InstanceId]);
            Assert.Equal(0, characterNet[fodderB.InstanceId]);
            Assert.False(await RawDb.CharacterExistsAsync(_fixture.ConnectionString, fodderA.InstanceId));

            // subject/expeditionLeader were never consumed: net ledger delta 1, row still present
            // (evolve updates the subject's row in place rather than replacing it, so its
            // InstanceId's ledger entry is still the original +1 grant).
            Assert.Equal(1, characterNet[subject.InstanceId]);
            Assert.True(await RawDb.CharacterExistsAsync(_fixture.ConnectionString, subject.InstanceId));
        }
    }
}
