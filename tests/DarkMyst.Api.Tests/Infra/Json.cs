using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>The same wire shape Program.cs configures the app to emit (Web casing + enums as
    /// strings) — tests deserialize responses with this, not with untuned defaults, so an enum
    /// field round-trips the same way a real client would see it.</summary>
    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
    }
}
