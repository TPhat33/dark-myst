using System;
using DarkMyst.Api.Battles;
using DarkMyst.Api.Content;
using DarkMyst.Api.Evolve;
using DarkMyst.Api.Expeditions;
using DarkMyst.Api.Idempotency;
using DarkMyst.Api.Teams;

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

                case IdempotencyKeyReusedException keyReused:
                    return (409, new { error = "idempotency_key_reused", message = keyReused.Message });

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
