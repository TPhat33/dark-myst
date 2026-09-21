using System;

namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// One completed evolve. <c>docs/03-evolve-spec.md</c> requires this: "`InstanceId` is never
    /// reused, even once a character has been spent as material — history can always point back to
    /// what disappeared." <see cref="FodderInstanceIdsJson"/> keeps pointing at instance ids whose
    /// <see cref="OwnedCharacterEntity"/> row no longer exists (fodder rows are deleted on evolve,
    /// not tombstoned) — that is by design, not a dangling reference bug.
    /// </summary>
    public sealed class EvolveHistoryEntity
    {
        public long Id { get; set; }

        public string OwnerId { get; set; }

        public string SubjectInstanceId { get; set; }

        public string FromCharacterId { get; set; }

        public string ResultCharacterId { get; set; }

        public string ResultInstanceId { get; set; }

        /// <summary>JSON array of the fodder instance ids consumed. See type remarks.</summary>
        public string FodderInstanceIdsJson { get; set; }

        public string Focus { get; set; }

        public string ContentVersion { get; set; }

        public int GoldSpent { get; set; }

        /// <summary>JSON object of materialId -&gt; amount spent.</summary>
        public string MaterialsSpentJson { get; set; }

        public int GainedBonusPerMille { get; set; }

        public int TotalBonusPerMille { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
