using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// Direct SQL against the test database, deliberately bypassing every service in
    /// server/DarkMyst.Api. This is how the ledger-reconciliation and content-version tests get at
    /// ground truth (or plant a fixture row) without trusting the very code under test to report
    /// on itself.
    /// </summary>
    public static class RawDb
    {
        public static async Task<int> GetGoldAsync(string connectionString, string accountId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT gold FROM accounts WHERE id = @id", connection);
            cmd.Parameters.AddWithValue("id", accountId);
            return (int)await cmd.ExecuteScalarAsync();
        }

        public static async Task<int> GetLedgerGoldSumAsync(string connectionString, string accountId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COALESCE(SUM(delta), 0) FROM ledger_entries WHERE account_id = @id AND kind = 'Gold'", connection);
            cmd.Parameters.AddWithValue("id", accountId);
            return (int)(long)await cmd.ExecuteScalarAsync();
        }

        public static async Task<Dictionary<string, int>> GetMaterialAmountsAsync(string connectionString, string accountId)
        {
            var result = new Dictionary<string, int>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT material_id, amount FROM inventory_materials WHERE account_id = @id", connection);
            cmd.Parameters.AddWithValue("id", accountId);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = reader.GetInt32(1);
            }

            return result;
        }

        public static async Task<Dictionary<string, int>> GetLedgerMaterialSumsAsync(string connectionString, string accountId)
        {
            var result = new Dictionary<string, int>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT ref_id, SUM(delta) FROM ledger_entries WHERE account_id = @id AND kind = 'Material' GROUP BY ref_id",
                connection);
            cmd.Parameters.AddWithValue("id", accountId);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = (int)reader.GetInt64(1);
            }

            return result;
        }

        /// <summary>Net character movement per instance id: +1 with no matching -1 means it should
        /// currently exist; a net of 0 means it was granted and later consumed as evolve fodder.</summary>
        public static async Task<Dictionary<string, int>> GetLedgerCharacterNetAsync(string connectionString, string accountId)
        {
            var result = new Dictionary<string, int>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT ref_id, SUM(delta) FROM ledger_entries WHERE account_id = @id AND kind = 'Character' GROUP BY ref_id",
                connection);
            cmd.Parameters.AddWithValue("id", accountId);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = (int)reader.GetInt64(1);
            }

            return result;
        }

        public static async Task<bool> CharacterExistsAsync(string connectionString, string instanceId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM owned_characters WHERE instance_id = @id", connection);
            cmd.Parameters.AddWithValue("id", instanceId);
            return (long)await cmd.ExecuteScalarAsync() > 0;
        }

        public static async Task<int> GetExpeditionLogLengthAsync(string connectionString, string runId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT state_json FROM expedition_runs WHERE id = @id", connection);
            cmd.Parameters.AddWithValue("id", runId);
            string stateJson = (string)await cmd.ExecuteScalarAsync();

            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(stateJson);
            return doc.RootElement.GetProperty("log").GetArrayLength();
        }

        /// <summary>Plants a run row referencing a content version the server's registry has never
        /// heard of — the only way to exercise "resuming after the pinned version is retired"
        /// without a real content-publishing pipeline to retire one through.</summary>
        public static async Task InsertOrphanedExpeditionRunAsync(
            string connectionString, string runId, string ownerId, string retiredContentVersion)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            // xmin (the row-version column, see ApiDbContext's remarks on ExpeditionRunEntity.Version)
            // is Postgres's own system column and is assigned automatically — never listed here.
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO expedition_runs
                    (id, owner_id, stage_id, content_version, status, lifecycle, state_json, created_at, updated_at)
                  VALUES
                    (@id, @owner, 'stg_ashfields', @version, 'InProgress', 'Active', '{}', now(), now())",
                connection);
            cmd.Parameters.AddWithValue("id", runId);
            cmd.Parameters.AddWithValue("owner", ownerId);
            cmd.Parameters.AddWithValue("version", retiredContentVersion);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<int> GetTelemetryEventCountAsync(string connectionString, string accountId, string type = null)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            string sql = "SELECT COUNT(*) FROM telemetry_events WHERE account_id = @id"
                + (type != null ? " AND type = @type" : string.Empty);
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("id", accountId);
            if (type != null)
            {
                cmd.Parameters.AddWithValue("type", type);
            }

            return (int)(long)await cmd.ExecuteScalarAsync();
        }

        /// <summary>Deletes the account row directly — the cascade-delete test's only way to
        /// exercise <c>ON DELETE CASCADE</c> without a real account-deletion endpoint (none exists
        /// yet; docs/08-metrics.md still requires the column support it). Clears
        /// <c>owned_characters</c> first: that table's own FK to <c>accounts</c> has no cascade
        /// (deleting a player's characters is a real feature this round does not build), and would
        /// otherwise block the delete this helper exists to perform.</summary>
        public static async Task DeleteAccountAsync(string connectionString, string accountId)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var clearCharacters = new NpgsqlCommand("DELETE FROM owned_characters WHERE owner_id = @id", connection))
            {
                clearCharacters.Parameters.AddWithValue("id", accountId);
                await clearCharacters.ExecuteNonQueryAsync();
            }

            await using var cmd = new NpgsqlCommand("DELETE FROM accounts WHERE id = @id", connection);
            cmd.Parameters.AddWithValue("id", accountId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Backdates the most recently written telemetry event of a given type for an
        /// account — the only way a test can control "days from obtained to first use" without
        /// waiting real days between the two calls that produce those two events.</summary>
        public static async Task BackdateLatestTelemetryEventAsync(
            string connectionString, string accountId, string type, DateTimeOffset occurredAt)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"UPDATE telemetry_events SET occurred_at = @occurredAt
                  WHERE id = (
                    SELECT id FROM telemetry_events
                    WHERE account_id = @id AND type = @type
                    ORDER BY id DESC LIMIT 1)",
                connection);
            cmd.Parameters.AddWithValue("id", accountId);
            cmd.Parameters.AddWithValue("type", type);
            cmd.Parameters.AddWithValue("occurredAt", occurredAt);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
