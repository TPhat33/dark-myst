using System;

namespace DarkMyst.Api.Data.Entities
{
    public enum ContentPublishKind
    {
        Publish = 0,
        Rollback = 1
    }

    /// <summary>
    /// The audit trail acceptance criterion asks for: one append-only row per successful publish
    /// or rollback, naming who did it and when. This is metadata about a change to
    /// <c>content/</c> — it never holds the content itself (that stays the single source of truth
    /// on disk, and in <c>content/_history/&lt;version&gt;/</c> for prior versions), so there is no
    /// second copy of game data living in Postgres.
    /// </summary>
    public sealed class ContentPublishEntity
    {
        public long Id { get; set; }

        public ContentPublishKind Kind { get; set; }

        public string Version { get; set; }

        public string PreviousVersion { get; set; }

        public string PublishedByAdminId { get; set; }

        public string Notes { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
