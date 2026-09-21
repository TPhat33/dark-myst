using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DarkMyst.Api.Idempotency
{
    /// <summary>What a guarded operation hands back to be stored and returned.</summary>
    public readonly struct IdempotentOperationResult
    {
        public IdempotentOperationResult(int statusCode, object body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public int StatusCode { get; }

        public object Body { get; }
    }

    /// <summary>What every idempotency-guarded call to the API ultimately returns.</summary>
    public readonly struct IdempotencyOutcome
    {
        public IdempotencyOutcome(int statusCode, string bodyJson, bool wasReplayed)
        {
            StatusCode = statusCode;
            BodyJson = bodyJson;
            WasReplayed = wasReplayed;
        }

        public int StatusCode { get; }

        public string BodyJson { get; }

        /// <summary>True when this call found and returned a previous attempt's stored result
        /// rather than running <c>operation</c> — the exact case the acceptance criterion is about
        /// ("a repeated request must never lose or duplicate anything").</summary>
        public bool WasReplayed { get; }
    }

    /// <summary>Raised when the same idempotency key is reused with a materially different
    /// request body. That is a client bug, not a legitimate retry, so it is refused rather than
    /// silently served whichever attempt happened to run first.</summary>
    public sealed class IdempotencyKeyReusedException : Exception
    {
        public IdempotencyKeyReusedException(string key)
            : base("Idempotency key '" + key + "' was already used for a different request body.")
        {
        }
    }

    /// <summary>
    /// The single shared mechanism every mutating endpoint routes through (docs/10-backend-spec.md).
    /// No endpoint re-implements its own duplicate check; each one calls <see cref="ExecuteAsync"/>
    /// and lets it decide whether <c>operation</c> runs at all.
    /// <para>
    /// The whole guarded call — reserving the key, running <c>operation</c>, and recording its
    /// result — is <b>one database transaction</b>. That is the entire mechanism:
    /// </para>
    /// <list type="bullet">
    /// <item>If <c>operation</c> throws, nothing commits, including the reservation itself — the
    /// key is exactly as unused as before the call, and a retry (with the same or a new key) starts
    /// clean. No mutation ever partially happens.</item>
    /// <item>If it returns, the mutation it performed and the stored response are the same commit.
    /// A crash between "the write happened" and "the response was recorded" is impossible because
    /// there is no gap: either both are visible or neither is.</item>
    /// <item>Two requests racing on the same key both try to insert the same primary key
    /// (account, endpoint, key). Postgres serializes that at the row level: the loser's insert
    /// either blocks until the winner's transaction resolves (and then fails with a unique-key
    /// error once the winner commits), or succeeds outright if the winner rolled back. Either way,
    /// <c>operation</c> runs at most once per key — this needs no application-level locking or
    /// polling loop, because the database is already doing that serialization for us.</item>
    /// </list>
    /// </summary>
    public sealed class IdempotencyService
    {
        private readonly ApiDbContext _db;
        private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

        // The same options Program.cs configures the whole app's JSON output with (Web casing,
        // enums as strings) — without sharing this, a response stored here would serialize with
        // System.Text.Json's untuned defaults (PascalCase) while every other endpoint's response
        // is camelCase, and a client would see the wire format change depending on whether a
        // request happened to be a replay.
        public IdempotencyService(ApiDbContext db, System.Text.Json.JsonSerializerOptions jsonOptions)
        {
            _db = db;
            _jsonOptions = jsonOptions;
        }

        public static string HashPayload(string payload)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        public async Task<IdempotencyOutcome> ExecuteAsync(
            string accountId,
            string endpoint,
            string idempotencyKey,
            string requestPayload,
            Func<Task<IdempotentOperationResult>> operation,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ArgumentException("An Idempotency-Key is required.", nameof(idempotencyKey));
            }

            string requestHash = HashPayload(requestPayload);

            // Fast path: this exact key already has a stored result (the ordinary "client resent
            // because it never saw our first response" case). No transaction needed to read it.
            IdempotencyOutcome? already = await TryReadCompletedAsync(accountId, endpoint, idempotencyKey, requestHash, ct);
            if (already.HasValue)
            {
                return already.Value;
            }

            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
                await _db.Database.BeginTransactionAsync(ct);

            var record = new IdempotencyRecordEntity
            {
                AccountId = accountId,
                Endpoint = endpoint,
                Key = idempotencyKey,
                RequestHash = requestHash,
                Status = IdempotencyStatus.InProgress,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.IdempotencyRecords.Add(record);

            try
            {
                // The INSERT is where a concurrent twin's identical reservation is resolved: this
                // either succeeds (we are the one running `operation`), blocks until the other
                // transaction commits or rolls back and then fails, or fails immediately if the
                // other transaction already committed. There is no in-between state visible here.
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                // The row we collided with is necessarily either committed by now (we would not
                // have failed with a duplicate key otherwise) — read and return it.
                IdempotencyOutcome? winner = await TryReadCompletedAsync(accountId, endpoint, idempotencyKey, requestHash, ct);
                if (winner.HasValue)
                {
                    return winner.Value;
                }

                // Extremely unlikely: the winner rolled back in the instant between our failed
                // insert and this read. Surface the original conflict rather than guess.
                throw;
            }

            IdempotentOperationResult result = await operation();
            string bodyJson = JsonSerializer.Serialize(result.Body, _jsonOptions);

            record.Status = IdempotencyStatus.Completed;
            record.ResponseStatusCode = result.StatusCode;
            record.ResponseBody = bodyJson;
            record.CompletedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new IdempotencyOutcome(result.StatusCode, bodyJson, wasReplayed: false);
        }

        private async Task<IdempotencyOutcome?> TryReadCompletedAsync(
            string accountId, string endpoint, string key, string requestHash, CancellationToken ct)
        {
            IdempotencyRecordEntity existing = await _db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.AccountId == accountId && r.Endpoint == endpoint && r.Key == key, ct);

            if (existing == null)
            {
                return null;
            }

            if (existing.RequestHash != requestHash)
            {
                throw new IdempotencyKeyReusedException(key);
            }

            if (existing.Status == IdempotencyStatus.InProgress)
            {
                // Only reachable if a prior attempt's process died between committing the
                // reservation and committing the completion — impossible under this service's own
                // single-transaction design (reservation and completion are the same commit), but
                // kept as a safe, explicit refusal rather than an assumption if that ever changes.
                return new IdempotencyOutcome(
                    409, JsonSerializer.Serialize(new { error = "still_processing", idempotencyKey = key }), wasReplayed: true);
            }

            return new IdempotencyOutcome(existing.ResponseStatusCode, existing.ResponseBody, wasReplayed: true);
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
        }
    }
}
