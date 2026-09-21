using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Content;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>Seeding via the debug-grant endpoints (server/DarkMyst.Api/Debug/DebugGrants.cs) —
    /// the stand-in for a shop/gacha system, see that file's remarks for why it exists.</summary>
    public static class Seed
    {
        public static async Task<OwnedCharacter> GrantCharacterAsync(
            HttpClient client, string token, string characterId, int level, string focus = null)
        {
            HttpResponseMessage response = await client.ApiPost(
                "/debug/grant-character", token, "seed-char-" + System.Guid.NewGuid(),
                new { characterId, level, focus });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OwnedCharacter>(Json.Options);
        }

        public static async Task GrantGoldAsync(HttpClient client, string token, int amount)
        {
            HttpResponseMessage response = await client.ApiPost(
                "/debug/grant-gold", token, "seed-gold-" + System.Guid.NewGuid(), new { amount });
            response.EnsureSuccessStatusCode();
        }

        public static async Task GrantMaterialAsync(HttpClient client, string token, string materialId, int amount)
        {
            HttpResponseMessage response = await client.ApiPost(
                "/debug/grant-material", token, "seed-mat-" + System.Guid.NewGuid(), new { materialId, amount });
            response.EnsureSuccessStatusCode();
        }
    }
}
