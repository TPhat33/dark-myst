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
    }
}
