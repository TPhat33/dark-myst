using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// Extra status the API layer tracks on top of <c>DarkMyst.Expedition.Model.RunStatus</c>
    /// (InProgress/Cleared/Failed). Abandon is not part of the library's own state machine — it is
    /// purely a server-side "stop accepting further choices on this run" — so it lives here
    /// instead of being smuggled into the library's enum.
    /// </summary>
    public enum RunLifecycle
    {
        Active = 0,
        Abandoned = 1
    }

    /// <summary>
    /// The server-owned record of one in-flight (or finished) expedition run. <see cref="StateJson"/>
    /// is exactly what <c>ExpeditionRun.Serialize()</c> produces — the server stores it verbatim
    /// and hands it back to <c>ExpeditionRun.Resume</c> rather than reconstructing state by hand.
    /// <see cref="ContentVersion"/> mirrors the pin already inside that JSON so it can be queried
    /// and validated against the content registry without deserializing first.
    /// </summary>
    public sealed class ExpeditionRunEntity
    {
        public string Id { get; set; }

        public string OwnerId { get; set; }

        public string StageId { get; set; }

        public string ContentVersion { get; set; }

        /// <summary>Library-level status, mirrored from state for querying. Source of truth is
        /// still <see cref="StateJson"/>; this column exists so a listing query need not
        /// deserialize every row.</summary>
        public string Status { get; set; }

        public RunLifecycle Lifecycle { get; set; }

        public string StateJson { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>Optimistic concurrency token. EF/Npgsql bumps this on every update; a second
        /// writer racing on the same row gets a <c>DbUpdateConcurrencyException</c> instead of
        /// silently overwriting the first writer's outcome — the same node cannot resolve twice.</summary>
        public uint Version { get; set; }
    }
}
