#!/usr/bin/env bash
# Launches a real DarkMyst.Api instance for Playwright to drive, against:
#   - a scratch Postgres database (created here, dropped on exit) — never the shared dev
#     "darkmyst" database other manual runs might be using;
#   - an isolated copy of content/ (also created here) — so a publish/rollback the e2e test
#     performs can never touch, and leave dirty, the repo's real content/.
# This is what proves docs/11-admin-spec.md's publish/rollback loop end to end without ever
# risking the working tree or a shared database.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DB_NAME="darkmyst_e2e_$$"
WORK_DIR="$(mktemp -d -t darkmyst-e2e-XXXXXXXXXX)"
CONTENT_DIR="$WORK_DIR/content"

API_PID=""

cleanup() {
  # Bug found the hard way (leftover darkmyst_e2e_* databases after every Playwright run): this
  # trap never fired when the API was started with `exec dotnet run` at the tail of the script,
  # because exec *replaces* this shell process — there was no shell left alive to catch the
  # SIGTERM Playwright's webServer teardown sends and run this cleanup. Running it as a background
  # job (see bottom of file) and `wait`-ing on it keeps this script's own process, and therefore
  # this trap, alive for the whole run.
  if [ -n "$API_PID" ]; then
    kill "$API_PID" >/dev/null 2>&1 || true
    wait "$API_PID" 2>/dev/null || true
  fi
  # Postgres refuses DROP DATABASE while any backend still holds a connection to it — the same
  # reason tests/DarkMyst.Api.Tests/Infra/PostgresTestDatabase.cs terminates backends first rather
  # than trusting that killing the client process alone closes them in time.
  PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=0 \
    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid()" \
    >/dev/null 2>&1 || true
  PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=0 \
    -c "DROP DATABASE IF EXISTS \"$DB_NAME\"" >/dev/null 2>&1 || true
  rm -rf "$WORK_DIR" || true
}
trap cleanup EXIT

# Self-healing sweep of anything a *previous* run left behind, run every time before creating
# this run's own database. Found the hard way: Playwright's webServer teardown for a
# `command`-based server can kill the whole process group hard enough (observed: the dotnet
# child ends up a reparented zombie) that this script's own EXIT trap above never gets a chance to
# run at all — no in-script trap can protect against that, so cleanup happens at the *next* run's
# startup instead, not that run's own shutdown. This is why the database name and content-dir
# prefix below are both stable, greppable patterns rather than fully random.
PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=0 -tAc \
  "SELECT datname FROM pg_database WHERE datname LIKE 'darkmyst_e2e_%'" 2>/dev/null |
  while IFS= read -r stale; do
    [ -z "$stale" ] && continue
    PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=0 \
      -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$stale'" >/dev/null 2>&1 || true
    PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=0 \
      -c "DROP DATABASE IF EXISTS \"$stale\"" >/dev/null 2>&1 || true
  done
find /tmp -maxdepth 1 -name 'darkmyst-e2e-*' -mmin +10 -exec rm -rf {} + 2>/dev/null || true

mkdir -p "$CONTENT_DIR"
cp "$REPO_ROOT"/content/*.json "$CONTENT_DIR"/

# Read by e2e/admin-content-workflow.spec.ts so the test can assert directly against the files on
# disk (not just against API responses) that a publish/rollback actually changed content/.
echo -n "$CONTENT_DIR" > "$REPO_ROOT/admin/e2e/.content-dir"

echo "[run-api] scratch content dir: $CONTENT_DIR"
echo "[run-api] creating scratch database $DB_NAME"
PGPASSWORD=darkmyst_dev psql -h 127.0.0.1 -U darkmyst -d postgres -v ON_ERROR_STOP=1 \
  -c "CREATE DATABASE \"$DB_NAME\""

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:5099"
export ConnectionStrings__ApiDb="Host=127.0.0.1;Port=5432;Username=darkmyst;Password=darkmyst_dev;Database=$DB_NAME"
export Content__Directory="$CONTENT_DIR"
export Admin__AllowBootstrap=true

echo "[run-api] starting API on $ASPNETCORE_URLS"
dotnet run --project "$REPO_ROOT/server/DarkMyst.Api" -c Release --no-build &
API_PID=$!
wait "$API_PID"
