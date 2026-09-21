using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DarkMyst.Api.Tests.Infra;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// The security acceptance criterion, proven directly: a player bearer token must never reach
    /// an admin endpoint, and only a real admin token (from <c>/admin/bootstrap</c>, itself
    /// Development/Admin:AllowBootstrap-gated) does (server/DarkMyst.Api/Auth/AdminAuth.cs,
    /// docs/11-admin-spec.md).
    /// </summary>
    public sealed class AdminAuthTests : IClassFixture<ApiTestFixture>
    {
        private readonly ApiTestFixture _fixture;

        public AdminAuthTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task A_player_bearer_token_in_the_normal_Authorization_header_is_refused()
        {
            (_, string playerToken) = await _fixture.Client.CreateGuestAsync();

            var request = new HttpRequestMessage(HttpMethod.Get, "/admin/content/current");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", playerToken);
            // Deliberately no X-Admin-Token header at all: a client that only ever holds a player
            // token has nothing to put there.
            HttpResponseMessage response = await _fixture.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task A_players_own_token_copied_into_X_Admin_Token_is_still_refused()
        {
            (_, string playerToken) = await _fixture.Client.CreateGuestAsync();

            // Even if a client mistakenly forwarded its player token under the admin header name,
            // it is looked up in admin_accounts — a table it was never issued into — not accounts.
            HttpResponseMessage response = await _fixture.Client.AdminGet("/admin/content/current", playerToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Missing_admin_token_is_refused()
        {
            HttpResponseMessage response = await _fixture.Client.AdminGet("/admin/content/current", adminToken: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task A_real_bootstrapped_admin_token_is_accepted()
        {
            (_, string adminToken) = await _fixture.Client.CreateAdminAsync();

            HttpResponseMessage response = await _fixture.Client.AdminGet("/admin/content/current", adminToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Two_bootstrapped_admins_get_different_unique_tokens()
        {
            (string firstId, string firstToken) = await _fixture.Client.CreateAdminAsync("alice");
            (string secondId, string secondToken) = await _fixture.Client.CreateAdminAsync("bob");

            Assert.NotEqual(firstId, secondId);
            Assert.NotEqual(firstToken, secondToken);
        }
    }
}
