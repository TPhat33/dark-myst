using System;
using System.Threading.Tasks;
using Npgsql;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// Creates and drops one throwaway Postgres database per test fixture, using the
    /// CREATEDB-privileged dev role directly (Docker/Testcontainers is not available in this
    /// environment). Every test class that needs the database gets its own, so tests in different
    /// classes can never see each other's rows — the isolation Testcontainers would otherwise give
    /// for free.
    /// </summary>
    public static class PostgresTestDatabase
    {
        private const string AdminConnectionString =
            "Host=127.0.0.1;Port=5432;Username=darkmyst;Password=darkmyst_dev;Database=postgres";

        public static async Task<string> CreateAsync()
        {
            string dbName = "darkmyst_test_" + Guid.NewGuid().ToString("n");

            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();

            await using (var create = new NpgsqlCommand("CREATE DATABASE \"" + dbName + "\"", connection))
            {
                await create.ExecuteNonQueryAsync();
            }

            var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = dbName };
            return builder.ConnectionString;
        }

        public static async Task DropAsync(string connectionString)
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            string dbName = parsed.Database;

            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();

            // Postgres refuses DROP DATABASE while any session still holds a connection to it —
            // the test host's own connection pool can still have one open at this point.
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
                connection))
            {
                terminate.Parameters.AddWithValue("name", dbName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using (var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS \"" + dbName + "\"", connection))
            {
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
