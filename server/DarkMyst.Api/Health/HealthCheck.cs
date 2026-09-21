using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Data;

namespace DarkMyst.Api.Health
{
    /// <summary>What <c>GET /health</c> reports (Program.cs).</summary>
    public readonly struct HealthResult
    {
        public HealthResult(bool databaseReachable, string database)
        {
            DatabaseReachable = databaseReachable;
            Database = database;
        }

        /// <summary>True when Postgres answered. False for every failure mode (timeout, refused,
        /// wrong credentials, DNS) — a caller needs "can I trust this backend to write anything"
        /// not a taxonomy of why it cannot.</summary>
        public bool DatabaseReachable { get; }

        public string Database { get; }
    }

    /// <summary>
    /// The one check behind <c>GET /health</c>. Liveness is implicit — the process answering this
    /// request at all is the liveness signal, matching the usual liveness/readiness split (a
    /// process that is up but cannot reach its database is alive, just not ready to serve
    /// anything that touches data, which here is everything except this endpoint itself).
    /// Readiness is "can we reach Postgres", checked with EF Core's own
    /// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.CanConnectAsync"/>
    /// against the same <see cref="ApiDbContext"/> every request uses — not a hand-rolled ping,
    /// so a green health check means the actual connection the rest of the app relies on works,
    /// not just that /some/ connection to /some/ database succeeded.
    /// </summary>
    public static class HealthCheck
    {
        public static async Task<HealthResult> CheckAsync(ApiDbContext db, CancellationToken ct)
        {
            try
            {
                bool reachable = await db.Database.CanConnectAsync(ct);
                return new HealthResult(reachable, reachable ? "up" : "down");
            }
            catch
            {
                // CanConnectAsync itself is documented to return false rather than throw for most
                // failures, but a connection attempt can still throw synchronously in some
                // failure modes (e.g. an unparsable connection string) — either way, an unreachable
                // database is the only conclusion a caller of /health needs, not a 500.
                return new HealthResult(false, "down");
            }
        }
    }
}
