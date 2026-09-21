using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// Thin request-building helpers shared by every test class. Authorization and
    /// Idempotency-Key are always set per <see cref="HttpRequestMessage"/> rather than on the
    /// shared <see cref="HttpClient"/>'s default headers, so concurrent tests issuing requests as
    /// different accounts (or with different keys) on the same client can never race on shared
    /// mutable header state.
    /// </summary>
    public static class TestClient
    {
        public static async Task<(string AccountId, string Token)> CreateGuestAsync(this HttpClient client)
        {
            HttpResponseMessage response = await client.ApiPost("/accounts/guest");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<GuestResponse>();
            return (body.AccountId, body.AccessToken);
        }

        public static async Task<(string AdminId, string Token)> CreateAdminAsync(this HttpClient client, string name = "test-admin")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/admin/bootstrap")
            {
                Content = JsonContent.Create(new { name }, options: Json.Options)
            };
            HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<AdminBootstrapResponse>();
            return (body.AdminId, body.AccessToken);
        }

        /// <summary>Same shape as <see cref="ApiPost"/> but for the admin surface: the credential
        /// goes in <c>X-Admin-Token</c>, never <c>Authorization</c> — see Auth/AdminAuth.cs.</summary>
        public static Task<HttpResponseMessage> AdminPost(
            this HttpClient client, string path, string adminToken = null, string idempotencyKey = null, object body = null)
        {
            return SendAdminAsync(client, HttpMethod.Post, path, adminToken, idempotencyKey, body);
        }

        public static Task<HttpResponseMessage> AdminGet(this HttpClient client, string path, string adminToken)
        {
            return SendAdminAsync(client, HttpMethod.Get, path, adminToken, idempotencyKey: null, body: null);
        }

        private static Task<HttpResponseMessage> SendAdminAsync(
            HttpClient client, HttpMethod method, string path, string adminToken, string idempotencyKey, object body)
        {
            var request = new HttpRequestMessage(method, path);
            if (adminToken != null)
            {
                request.Headers.Add(AdminAuth.HeaderName, adminToken);
            }

            if (idempotencyKey != null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            if (body != null)
            {
                request.Content = JsonContent.Create(body, options: Json.Options);
            }
            else if (method != HttpMethod.Get)
            {
                request.Content = new StringContent(string.Empty);
            }

            return client.SendAsync(request);
        }

        private sealed record AdminBootstrapResponse(string AdminId, string Name, string AccessToken);

        // "Api"-prefixed to avoid any ambiguity with the built-in HttpClient/HttpContent
        // extension methods of the same short names (PostAsync/GetAsync/DeleteAsync).
        public static Task<HttpResponseMessage> ApiPost(
            this HttpClient client, string path, string token = null, string idempotencyKey = null, object body = null)
        {
            return SendAsync(client, HttpMethod.Post, path, token, idempotencyKey, body);
        }

        public static Task<HttpResponseMessage> ApiDelete(
            this HttpClient client, string path, string token, string idempotencyKey)
        {
            return SendAsync(client, HttpMethod.Delete, path, token, idempotencyKey, body: null);
        }

        public static Task<HttpResponseMessage> ApiGet(this HttpClient client, string path, string token)
        {
            return SendAsync(client, HttpMethod.Get, path, token, idempotencyKey: null, body: null);
        }

        private static Task<HttpResponseMessage> SendAsync(
            HttpClient client, HttpMethod method, string path, string token, string idempotencyKey, object body)
        {
            var request = new HttpRequestMessage(method, path);
            if (token != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (idempotencyKey != null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            if (body != null)
            {
                request.Content = JsonContent.Create(body, options: Json.Options);
            }
            else if (method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                // Every mutating endpoint reads the raw body for idempotency hashing
                // (ApiIo.ReadBodyAsync) — an empty string body is a valid, deliberate "no payload"
                // request, not a missing one.
                request.Content = new StringContent(string.Empty);
            }

            return client.SendAsync(request);
        }

        private sealed record GuestResponse(string AccountId, string AccessToken);
    }
}
