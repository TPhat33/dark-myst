using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Idempotency;
using Microsoft.AspNetCore.Http;

namespace DarkMyst.Api
{
    /// <summary>
    /// Small shared plumbing every minimal-API handler in Program.cs uses, so the
    /// idempotency-key / raw-body-hashing contract is followed identically everywhere rather than
    /// re-typed per endpoint. See docs/10-backend-spec.md for the wire contract this implements.
    /// </summary>
    public static class ApiIo
    {
        /// <summary>
        /// Reads the request body as both the raw string (what <see cref="IdempotencyService"/>
        /// hashes — it must see exactly what the client sent, not a round-tripped
        /// re-serialization) and the deserialized DTO.
        /// </summary>
        public static async Task<(T Body, string Raw)> ReadBodyAsync<T>(
            HttpRequest request, JsonSerializerOptions options, CancellationToken ct)
        {
            using var reader = new StreamReader(request.Body);
            string raw = await reader.ReadToEndAsync(ct);
            T body = string.IsNullOrEmpty(raw)
                ? default
                : JsonSerializer.Deserialize<T>(raw, options);
            return (body, raw ?? string.Empty);
        }

        public static string RequireIdempotencyKey(HttpRequest request)
        {
            string key = request.Headers["Idempotency-Key"];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The 'Idempotency-Key' header is required for this endpoint.");
            }

            return key;
        }

        /// <summary>Turns a stored/just-computed idempotency outcome into the actual HTTP
        /// response. The body was already serialized once by <see cref="IdempotencyService"/> (or
        /// on a prior attempt) — writing it back out verbatim, rather than deserializing and
        /// re-serializing, is what guarantees a replay is byte-identical to the original.</summary>
        public static IResult ToResult(IdempotencyOutcome outcome)
        {
            return Results.Content(outcome.BodyJson, "application/json", System.Text.Encoding.UTF8, outcome.StatusCode);
        }
    }
}
