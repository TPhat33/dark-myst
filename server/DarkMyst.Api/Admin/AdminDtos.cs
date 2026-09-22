using System.Collections.Generic;
using DarkMyst.Combat.Model;
using DarkMyst.Content;

namespace DarkMyst.Api.Admin
{
    // ------------------------------------------------------------------
    // POST /admin/bootstrap
    // ------------------------------------------------------------------

    public sealed record BootstrapRequest(string Name);

    // ------------------------------------------------------------------
    // GET /admin/content/current
    // ------------------------------------------------------------------

    /// <summary><c>RollbackableVersions</c> is every version this process can still restore
    /// content/ to — i.e. every <c>content/_history/&lt;version&gt;/</c> snapshot, not just the
    /// ones with a publish/rollback audit row (see the "GET /admin/content/versions" record below).
    /// This includes the very first version the tool was ever pointed at: publishing snapshots the
    /// *outgoing* version too, before overwriting it, so even a baseline that predates this tool
    /// entirely becomes reachable the moment anything is published on top of it.
    /// <para>
    /// Skills, enemies and encounters were added alongside characters in the same response
    /// (docs/11-admin-spec.md): each editor tab loads everything once from here, then round-trips
    /// only its own list back through validate/publish (see <see cref="AdminContentEditRequest"/>).
    /// </para>
    /// </summary>
    public sealed record AdminContentCurrentResponse(
        string Version,
        string RulesVersion,
        List<CharacterData> Characters,
        List<SkillDefinition> Skills,
        List<EnemyData> Enemies,
        List<EncounterData> Encounters,
        List<string> RollbackableVersions);

    // ------------------------------------------------------------------
    // POST /admin/content/validate, POST /admin/content/publish
    // ------------------------------------------------------------------

    /// <summary>
    /// The full, edited contents of whichever content type's tab is dirty — not a patch, and not
    /// necessarily every type at once. Exactly one of <see cref="Characters"/>,
    /// <see cref="Skills"/>, <see cref="Enemies"/> and <see cref="Encounters"/> is populated by a
    /// given editor tab; the others are left <c>null</c>, meaning "unchanged, read straight off
    /// disk" (see <c>AdminContentService.BuildStagedPack</c>). Whichever list <em>is</em> present
    /// is always the type's whole list, never a partial patch — the same rule characters has always
    /// followed (docs/11-admin-spec.md) — so every request is still validated as one complete
    /// <c>ContentPack</c>, the same way a hand-edit of the underlying JSON file would be.
    /// </summary>
    public sealed record AdminContentEditRequest(
        List<CharacterData> Characters,
        List<SkillDefinition> Skills,
        List<EnemyData> Enemies,
        List<EncounterData> Encounters,
        string Notes);

    public sealed record AdminValidateResponse(bool Valid, IReadOnlyList<string> Errors);

    public sealed record AdminPublishResponse(
        string Version,
        string PreviousVersion,
        int CharacterCount,
        int SkillCount,
        int EnemyCount,
        int EncounterCount,
        System.DateTimeOffset PublishedAt);

    // ------------------------------------------------------------------
    // POST /admin/content/rollback
    // ------------------------------------------------------------------

    public sealed record AdminRollbackRequest(string TargetVersion, string Notes);

    public sealed record AdminRollbackResponse(string Version, string PreviousVersion, System.DateTimeOffset PublishedAt);

    // ------------------------------------------------------------------
    // GET /admin/content/versions
    // ------------------------------------------------------------------

    public sealed record AdminVersionEntry(
        string Version, string PreviousVersion, string Kind, string PublishedByAdminId, string Notes, System.DateTimeOffset PublishedAt);

    // ------------------------------------------------------------------
    // GET /admin/content/diff
    // ------------------------------------------------------------------

    public sealed record AdminFieldChange(string Path, string Before, string After);

    public sealed record AdminCharacterDiff(string CharacterId, List<AdminFieldChange> Fields);

    public sealed record AdminContentDiffResponse(
        string From, string To, List<string> AddedCharacterIds, List<string> RemovedCharacterIds, List<AdminCharacterDiff> ChangedCharacters);

    // ------------------------------------------------------------------
    // POST /admin/content/sweep
    // ------------------------------------------------------------------

    /// <summary>
    /// A battle sweep against the currently published content — the same shape
    /// <c>simrunner sweep</c> takes on the command line (docs/07-testing-plan.md), run through the
    /// shared <c>DarkMyst.Sim.BattleSweepRunner</c> (docs/11-admin-spec.md "sweep button"). Level,
    /// seed and repeat all default the same way the CLI does; <c>Repeat</c> is additionally capped
    /// server-side (see <see cref="AdminSweepService"/>) since this one is reachable from a browser
    /// button rather than typed by hand.
    /// </summary>
    public sealed record AdminSweepRequest(
        string EncounterId, List<string> Roster, int Level = 20, ulong Seed = 1, int Repeat = 200);

    public sealed record AdminSweepSurvivorEntry(string CharacterId, int Survived);

    public sealed record AdminSweepResponse(
        string EncounterId,
        int Battles,
        int Wins,
        int Draws,
        double AverageRounds,
        List<AdminSweepSurvivorEntry> Survivors,
        int MaxRepeat);
}
