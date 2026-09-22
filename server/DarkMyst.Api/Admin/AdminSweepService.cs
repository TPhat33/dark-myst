using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Content;
using DarkMyst.Sim;
using Microsoft.Extensions.Configuration;

namespace DarkMyst.Api.Admin
{
    /// <summary>Thrown when a sweep is already running. Sweeps are CPU-heavy (many full battle
    /// simulations back to back), so this API only ever runs one at a time process-wide, on top of
    /// the per-request <see cref="AdminSweepService.MaxRepeat"/> cap — a second click, a second
    /// browser tab or a second admin all get this instead of piling more CPU-bound work onto the
    /// thread pool underneath whatever sweep is already in flight.</summary>
    public sealed class SweepAlreadyRunningException : Exception
    {
        public SweepAlreadyRunningException()
            : base("A sweep is already running. Wait for it to finish before starting another.")
        {
        }
    }

    /// <summary>Thrown when a sweep asks for more battles than <see cref="AdminSweepService.MaxRepeat"/>
    /// allows. The cap exists because this endpoint is one click away in a browser, unlike
    /// <c>simrunner sweep --repeat</c>, which someone has to type by hand and can run for as long
    /// as they like from a terminal — see docs/11-admin-spec.md's sweep design note.</summary>
    public sealed class SweepRepeatTooLargeException : Exception
    {
        public SweepRepeatTooLargeException(int requested, int max)
            : base(
                "repeat " + requested + " exceeds the admin sweep button's cap of " + max
                + ". Run `dotnet run --project tools/DarkMyst.SimRunner -- sweep --repeat " + requested
                + "` from a terminal for a larger sweep.")
        {
            Requested = requested;
            Max = max;
        }

        public int Requested { get; }

        public int Max { get; }
    }

    /// <summary>
    /// Runs a battle sweep against the currently published content pack for the admin tool's sweep
    /// button (docs/11-admin-spec.md, docs/05-content-pipeline.md's แก้ → validate → sweep →
    /// ตรวจทาน → เผยแพร่ workflow). Bounded two ways:
    /// <list type="bullet">
    /// <item><description><b>Repeat cap</b> (<c>Admin:MaxSweepRepeat</c>, default 500 — the
    /// largest repeat count docs/07-testing-plan.md documents for a hand-run sweep) — a request
    /// asking for more is refused with <see cref="SweepRepeatTooLargeException"/> before any
    /// battle runs.</description></item>
    /// <item><description><b>One sweep at a time, process-wide</b> — a
    /// <see cref="SemaphoreSlim"/> taken with a zero timeout, so a second concurrent request (a
    /// double click, a second tab, a second admin) is refused immediately with
    /// <see cref="SweepAlreadyRunningException"/> rather than queued behind the first one or left
    /// to run alongside it and double the CPU cost.</description></item>
    /// </list>
    /// A subprocess (shelling out to <c>simrunner</c>) was rejected in favour of this in-process
    /// library call: spawning an arbitrary process per web request is its own resource and
    /// security surface (process limits, argv handling, output parsing) that a shared library
    /// (<c>DarkMyst.Sim</c>) sidesteps entirely, and the two call sites were already close enough
    /// to identical that keeping them as separate implementations was the bigger risk.
    /// </summary>
    public sealed class AdminSweepService
    {
        private static readonly SemaphoreSlim SweepLock = new SemaphoreSlim(1, 1);

        private readonly ContentPackRegistry _registry;

        public AdminSweepService(ContentPackRegistry registry, IConfiguration configuration)
        {
            _registry = registry;
            MaxRepeat = configuration.GetValue<int?>("Admin:MaxSweepRepeat") ?? 500;
        }

        /// <summary>The most battles a single admin sweep request may run. Returned to the client
        /// on every response so the UI can show the limit, not just enforce it after a refusal.</summary>
        public int MaxRepeat { get; }

        public async Task<SweepResult> RunAsync(AdminSweepRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentException("A sweep request body is required.");
            }

            if (request.Repeat <= 0)
            {
                throw new ArgumentException("repeat must be positive.");
            }

            if (request.Repeat > MaxRepeat)
            {
                throw new SweepRepeatTooLargeException(request.Repeat, MaxRepeat);
            }

            if (request.Roster == null || request.Roster.Count == 0)
            {
                throw new ArgumentException("roster must name at least one character id.");
            }

            if (string.IsNullOrEmpty(request.EncounterId))
            {
                throw new ArgumentException("encounterId is required.");
            }

            // A zero timeout: this either acquires the lock right now or fails immediately. There
            // is deliberately no queueing — a caller that wants to run another sweep right after
            // this one finishes just clicks the button again.
            if (!await SweepLock.WaitAsync(0, ct))
            {
                throw new SweepAlreadyRunningException();
            }

            try
            {
                ContentPack pack = _registry.Latest;
                var sweepRequest = new SweepRequest
                {
                    EncounterId = request.EncounterId,
                    Roster = request.Roster,
                    Level = request.Level > 0 ? request.Level : 20,
                    Seed = request.Seed,
                    Repeat = request.Repeat
                };

                // Off the request-handling thread: a sweep at the cap is many hundreds of full
                // battle simulations, and this keeps that CPU-bound work from running inline on
                // whatever thread pool thread happened to pick up the request.
                return await Task.Run(() => BattleSweepRunner.Run(pack, sweepRequest, ct), ct);
            }
            finally
            {
                SweepLock.Release();
            }
        }

        public static AdminSweepResponse ToResponse(SweepResult result, int maxRepeat)
        {
            return new AdminSweepResponse(
                result.EncounterId,
                result.Battles,
                result.Wins,
                result.Draws,
                result.AverageRounds,
                result.Survivors.Select(s => new AdminSweepSurvivorEntry(s.CharacterId, s.Survived)).ToList(),
                maxRepeat);
        }
    }
}
