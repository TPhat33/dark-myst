using System;
using DarkMyst.Api.Admin;
using DarkMyst.Api.Battles;
using DarkMyst.Api.Content;
using DarkMyst.Api.Evolve;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Idempotency;
using DarkMyst.Api.Teams;
using DarkMyst.Content;

namespace DarkMyst.Api
{
    /// <summary>
    /// Maps every refusal exception this API defines to the clean HTTP status it should have been
    /// all along, in <c>app.UseExceptionHandler</c>. Every one of these is an expected outcome of a
    /// bad-but-well-formed request (someone else's character, a stale content version, a reused
    /// idempotency key with a different body) — none of them should ever look like a 500 to a
    /// client, because a client that treats "refused" and "broke" the same way cannot build a
    /// sensible retry policy.
    /// </summary>
    public static class ApiErrors
    {
        public static (int Status, object Body) Map(Exception ex)
        {
            switch (ex)
            {
                case UnauthorizedAccessException unauthorized:
                    return (401, new { error = "unauthorized", message = unauthorized.Message });

                case EvolveRefusedException evolveRefused:
                    return (409, new { error = "evolve_refused", blockers = evolveRefused.Blockers });

                case ExpeditionRefusedException expeditionRefused:
                    return (409, new { error = "expedition_refused", message = expeditionRefused.Message });

                case TeamRefusedException teamRefused:
                    return (409, new { error = "team_refused", message = teamRefused.Message });

                case BattleRefusedException battleRefused:
                    return (409, new { error = "battle_refused", message = battleRefused.Message });

                case ContentVersionUnavailableException versionUnavailable:
                    return (409, new { error = "content_version_unavailable", version = versionUnavailable.Version });

                // Raised by AdminContentService when edited content fails the same
                // ContentPack.Validate() the server and CI already run. There is no override flag
                // that lets a publish skip this — see docs/11-admin-spec.md.
                case ContentException contentInvalid:
                    return (422, new { error = "content_invalid", problems = contentInvalid.Problems });

                case IdempotencyKeyReusedException keyReused:
                    return (409, new { error = "idempotency_key_reused", message = keyReused.Message });

                // AdminSweepService: at most one sweep runs at a time process-wide, and a click
                // while one is already in flight is refused rather than queued or run alongside it
                // (docs/11-admin-spec.md "sweep button").
                case SweepAlreadyRunningException sweepRunning:
                    return (409, new { error = "sweep_in_progress", message = sweepRunning.Message });

                // AdminSweepService: a repeat above Admin:MaxSweepRepeat is refused before any
                // battle runs, not silently clamped, so a designer knows their request was capped.
                case SweepRepeatTooLargeException sweepTooLarge:
                    return (400, new
                    {
                        error = "sweep_repeat_too_large",
                        message = sweepTooLarge.Message,
                        requested = sweepTooLarge.Requested,
                        max = sweepTooLarge.Max
                    });

                case ArgumentException argument:
                    return (400, new { error = "bad_request", message = argument.Message });

                case InvalidOperationException invalidOperation:
                    return (400, new { error = "bad_request", message = invalidOperation.Message });

                default:
                    // Deliberately no ex.Message here: an unmapped exception is a bug, not a
                    // refusal, and its detail belongs in server logs, not in a client's hands.
                    return (500, new { error = "internal_error" });
            }
        }
    }
}
