using System;
using System.Text.Json;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;

namespace DarkMyst.Api.Telemetry
{
    /// <summary>
    /// The one place any <c>telemetry_events</c> row is created (docs/10-backend-spec.md's
    /// telemetry section). <see cref="Add"/> stages the row on the caller's already-open
    /// <see cref="ApiDbContext"/> and deliberately never calls <c>SaveChangesAsync</c> itself —
    /// exactly the same shape as <see cref="Ledger.LedgerService"/>. Whichever <c>SaveChangesAsync</c>
    /// (almost always <c>IdempotencyService.ExecuteAsync</c>'s own, inside its transaction) commits
    /// the gameplay change this event describes commits the event with it, in the same statement
    /// batch. There is no code path that can produce the event without the state change, or the
    /// state change without the event: either both are in the database or neither is.
    /// </summary>
    public sealed class TelemetryWriter
    {
        private readonly ApiDbContext _db;
        private readonly JsonSerializerOptions _jsonOptions;

        public TelemetryWriter(ApiDbContext db, JsonSerializerOptions jsonOptions)
        {
            _db = db;
            _jsonOptions = jsonOptions;
        }

        /// <summary>Stages one event. <paramref name="accountId"/> may be null only for an event
        /// that is not about a single account (none of the catalogue in docs/10 is, today).</summary>
        public void Add(string accountId, string type, string contentVersion, string rulesVersion, object payload)
        {
            if (string.IsNullOrEmpty(type))
            {
                throw new ArgumentException("Telemetry event type is required.", nameof(type));
            }

            _db.TelemetryEvents.Add(new TelemetryEventEntity
            {
                AccountId = accountId,
                Type = type,
                OccurredAt = DateTimeOffset.UtcNow,
                ContentVersion = contentVersion,
                RulesVersion = rulesVersion,
                PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions)
            });
        }
    }
}
