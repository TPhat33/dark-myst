using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Accounts;
using DarkMyst.Api.Tests.Infra;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// docs/04-economy-spec.md's called-out case: a guest save on a new device links to a durable
    /// identity that already has a save. Both saves must be retained and presented, not one
    /// silently destroyed.
    /// </summary>
    public sealed class AccountLinkingTests : IClassFixture<ApiTestFixture>
    {
        private readonly ApiTestFixture _fixture;

        public AccountLinkingTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Linking_a_never_seen_identity_succeeds_immediately()
        {
            var client = _fixture.Client;
            (string accountId, string token) = await client.CreateGuestAsync();

            var response = await client.ApiPost("/accounts/link/start", token, "link-fresh",
                new { provider = "google", externalToken = "g-" + accountId });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<LinkStartDto>(Json.Options);
            Assert.Equal("linked", body.Status);
            Assert.Equal(accountId, body.AccountId);
        }

        [Fact]
        public async Task Linking_to_an_identity_that_already_has_a_save_presents_both_and_retains_the_loser()
        {
            var client = _fixture.Client;

            (string firstAccountId, string firstToken) = await client.CreateGuestAsync();
            await Seed.GrantGoldAsync(client, firstToken, 777);
            await Seed.GrantCharacterAsync(client, firstToken, "chr_ashen_knight_i", level: 12);

            const string externalId = "shared-google-id";
            var firstLink = await client.ApiPost("/accounts/link/start", firstToken, "link-first",
                new { provider = "google", externalToken = externalId });
            firstLink.EnsureSuccessStatusCode();

            // A second, brand-new guest (e.g. a fresh install) tries to link the same identity.
            (string secondAccountId, string secondToken) = await client.CreateGuestAsync();
            await Seed.GrantGoldAsync(client, secondToken, 111);

            var secondLink = await client.ApiPost("/accounts/link/start", secondToken, "link-second",
                new { provider = "google", externalToken = externalId });
            secondLink.EnsureSuccessStatusCode();
            var conflict = await secondLink.Content.ReadFromJsonAsync<LinkStartDto>(Json.Options);
            Assert.Equal("conflict", conflict.Status);
            Assert.NotNull(conflict.PendingLinkId);
            Assert.Equal(secondAccountId, conflict.GuestSave.AccountId);
            Assert.Equal(firstAccountId, conflict.ExistingSave.AccountId);
            Assert.Equal(777, conflict.ExistingSave.Gold);
            Assert.Equal(111, conflict.GuestSave.Gold);
            Assert.Equal(1, conflict.ExistingSave.CharacterCount);

            var confirmResponse = await client.ApiPost("/accounts/link/confirm", secondToken, "confirm-keep-existing",
                new { pendingLinkId = conflict.PendingLinkId, choice = "KeepExistingSave" });
            confirmResponse.EnsureSuccessStatusCode();
            var confirmed = await confirmResponse.Content.ReadFromJsonAsync<LinkConfirmDto>(Json.Options);
            Assert.Equal(firstAccountId, confirmed.AccountId);

            // The losing (second) account's token is dead — its data is retained, not deleted, but
            // it can no longer authenticate as itself (docs/04-economy-spec.md: "เก็บเซฟที่ถูกทิ้ง
            // ไว้ระยะหนึ่งเผื่อเลือกผิด" — retained, not destroyed).
            var deadTokenResponse = await client.ApiGet("/accounts/me", secondToken);
            Assert.Equal(HttpStatusCode.Unauthorized, deadTokenResponse.StatusCode);

            // The winning (first, pre-existing) account's own token still authenticates fine, and
            // its data (gold, character) is untouched by the whole exchange.
            var winnerResponse = await client.ApiGet("/accounts/me", firstToken);
            winnerResponse.EnsureSuccessStatusCode();
        }

        private sealed record LinkStartDto(
            string Status, string AccountId, string PendingLinkId,
            AccountSaveSummary GuestSave, AccountSaveSummary ExistingSave);

        private sealed record LinkConfirmDto(string AccountId, string AccessToken);
    }
}
