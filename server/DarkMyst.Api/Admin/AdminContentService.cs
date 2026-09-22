using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using DarkMyst.Api.Data;
using DarkMyst.Api.Data.Entities;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DarkMyst.Api.Admin
{
    /// <summary>
    /// The whole "แก้ → validate → sweep → ตรวจทาน → เผยแพร่ → (ย้อนกลับได้)" workflow
    /// (docs/05-content-pipeline.md), scoped to characters for this round (docs/11-admin-spec.md
    /// "deliberately deferred"). Every operation here reads and writes the exact same
    /// <c>content/</c> directory <see cref="ContentPackRegistry"/> loaded at startup, through the
    /// exact same <see cref="ContentPack.Load"/> / <see cref="ContentPack.Validate"/> the server and
    /// CI already use — there is no second content model, no shadow database table for game data,
    /// and no way to publish content that fails validation (no override flag exists to skip it).
    /// <para>
    /// <c>content/_history/&lt;version&gt;/</c> holds a full, immutable snapshot of every version
    /// this tool has ever published or rolled back to. It is what rollback restores from, and what
    /// lets <see cref="Diff"/> compare two versions that are not both resident in
    /// <see cref="ContentPackRegistry"/> right now (e.g. after a process restart, which only
    /// reloads whatever <c>content/</c> currently holds as the sole in-memory version — see
    /// <see cref="GetPack"/>).
    /// </para>
    /// <para>
    /// Publish and rollback are each guarded by a process-wide lock, not a database row lock like
    /// <c>EvolveService</c>/<c>ExpeditionService</c> use for player-economy races. That is a
    /// deliberate, smaller guarantee: content publishing is a rare, low-concurrency, small-team
    /// operation, not a high-frequency player action, and this API always runs as a single process
    /// in this phase. Two publishes racing across separate processes (a horizontally-scaled
    /// deployment) would still be able to race the version bump — flagged here, not solved, exactly
    /// as docs/10-backend-spec.md flags <c>ContentPackRegistry</c> never retiring old versions.
    /// </para>
    /// </summary>
    public sealed class AdminContentService
    {
        private static readonly SemaphoreSlim PublishLock = new SemaphoreSlim(1, 1);

        private readonly ApiDbContext _db;
        private readonly ContentPackRegistry _registry;
        private readonly string _root;

        public AdminContentService(ApiDbContext db, ContentPackRegistry registry, ContentRootPath rootPath)
        {
            _db = db;
            _registry = registry;
            _root = rootPath.Directory;
        }

        private string HistoryRoot => Path.Combine(_root, "_history");

        public AdminContentCurrentResponse GetCurrent()
        {
            ContentPack pack = _registry.Latest;
            List<CharacterData> characters = pack.Characters.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            List<SkillDefinition> skills = pack.Skills.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            List<EnemyData> enemies = pack.Enemies.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            List<EncounterData> encounters = pack.Encounters.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            List<string> rollbackable = ListSnapshotVersions()
                .Where(v => !string.Equals(v, pack.Version, StringComparison.Ordinal))
                .OrderByDescending(v => v, StringComparer.Ordinal)
                .ToList();

            return new AdminContentCurrentResponse(
                pack.Version, pack.Manifest.RulesVersion, characters, skills, enemies, encounters, rollbackable);
        }

        /// <summary>Every version with a full snapshot under <c>content/_history/</c> — every
        /// version this tool can actually restore <c>content/</c> to, whether or not it has its own
        /// publish/rollback audit row (the very first version this tool was ever run against never
        /// does, since nothing published <em>it</em> — see <see cref="AdminContentCurrentResponse"/>
        /// remarks).</summary>
        private List<string> ListSnapshotVersions()
        {
            if (!Directory.Exists(HistoryRoot))
            {
                return new List<string>();
            }

            return Directory.GetDirectories(HistoryRoot).Select(Path.GetFileName).ToList();
        }

        public AdminValidateResponse Validate(
            List<CharacterData> characters, List<SkillDefinition> skills, List<EnemyData> enemies,
            List<EncounterData> encounters)
        {
            try
            {
                BuildStagedPack(characters, skills, enemies, encounters, _registry.Latest, overrideVersion: null);
                return new AdminValidateResponse(true, Array.Empty<string>());
            }
            catch (ContentException ex)
            {
                return new AdminValidateResponse(false, ex.Problems);
            }
        }

        public async Task<AdminPublishResponse> PublishAsync(
            string adminId,
            List<CharacterData> characters,
            List<SkillDefinition> skills,
            List<EnemyData> enemies,
            List<EncounterData> encounters,
            string notes,
            CancellationToken ct)
        {
            await PublishLock.WaitAsync(ct);
            try
            {
                ContentPack current = _registry.Latest;
                string oldVersion = current.Version;
                string newVersion = NextUnusedVersion(oldVersion);

                // Throws ContentException (no disk write happens first) if whichever type(s) were
                // edited, combined with every other unchanged content file, would not pass the same
                // Validate() the server and CI already run. This is the "no override flag" rule:
                // there is no code path from here to a disk write that skips this call.
                BuildStagedPack(characters, skills, enemies, encounters, current, newVersion);

                SnapshotIfMissing(oldVersion);

                string manifestJson = BuildManifestJson(current.Manifest, newVersion);
                File.WriteAllText(Path.Combine(_root, "manifest.json"), manifestJson);

                // Only the type(s) this request actually edited get a new file written — every
                // other content file is left exactly as it is on disk, matching what BuildStagedPack
                // just validated against.
                if (characters != null)
                {
                    File.WriteAllText(
                        Path.Combine(_root, current.Manifest.Characters), SerializeNamedList("characters", characters));
                }

                if (skills != null)
                {
                    File.WriteAllText(
                        Path.Combine(_root, current.Manifest.Skills), SerializeNamedList("skills", skills));
                }

                if (enemies != null)
                {
                    File.WriteAllText(
                        Path.Combine(_root, current.Manifest.Enemies), SerializeNamedList("enemies", enemies));
                }

                if (encounters != null)
                {
                    File.WriteAllText(
                        Path.Combine(_root, current.Manifest.Encounters), SerializeNamedList("encounters", encounters));
                }

                // Snapshot the version we just published too, so a later rollback can always reach
                // it again even after further publishes move content/ on past it.
                SnapshotIfMissing(newVersion);

                // Re-read from disk rather than trust any in-memory list: this is what proves
                // content/ itself changed, not just this request's view of it.
                ContentPack fresh = ContentPack.LoadFromDirectory(_root);
                _registry.Register(fresh);
                _registry.SetLatest(newVersion);

                var audit = new ContentPublishEntity
                {
                    Kind = ContentPublishKind.Publish,
                    Version = newVersion,
                    PreviousVersion = oldVersion,
                    PublishedByAdminId = adminId,
                    Notes = notes,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.ContentPublishes.Add(audit);
                await _db.SaveChangesAsync(ct);

                return new AdminPublishResponse(
                    newVersion, oldVersion,
                    fresh.Characters.Count, fresh.Skills.Count, fresh.Enemies.Count, fresh.Encounters.Count,
                    audit.CreatedAt);
            }
            finally
            {
                PublishLock.Release();
            }
        }

        public async Task<AdminRollbackResponse> RollbackAsync(
            string adminId, string targetVersion, string notes, CancellationToken ct)
        {
            await PublishLock.WaitAsync(ct);
            try
            {
                string currentVersion = _registry.LatestVersion;
                if (string.Equals(targetVersion, currentVersion, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Version '" + targetVersion + "' is already the published version.");
                }

                // Loads (and re-validates) the target — throws ContentVersionUnavailableException,
                // mapped to a clean 409, if no snapshot of it exists to roll back to.
                GetPack(targetVersion);

                // So the version we are rolling back *from* can still be rolled forward to later.
                SnapshotIfMissing(currentVersion);

                string historyDir = Path.Combine(HistoryRoot, targetVersion);
                foreach (string file in Directory.GetFiles(historyDir))
                {
                    File.Copy(file, Path.Combine(_root, Path.GetFileName(file)), overwrite: true);
                }

                ContentPack fresh = ContentPack.LoadFromDirectory(_root);
                if (!_registry.LoadedVersions.Contains(targetVersion, StringComparer.Ordinal))
                {
                    _registry.Register(fresh);
                }

                _registry.SetLatest(targetVersion);

                var audit = new ContentPublishEntity
                {
                    Kind = ContentPublishKind.Rollback,
                    Version = targetVersion,
                    PreviousVersion = currentVersion,
                    PublishedByAdminId = adminId,
                    Notes = notes,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.ContentPublishes.Add(audit);
                await _db.SaveChangesAsync(ct);

                return new AdminRollbackResponse(targetVersion, currentVersion, audit.CreatedAt);
            }
            finally
            {
                PublishLock.Release();
            }
        }

        public async Task<List<AdminVersionEntry>> ListVersionsAsync(CancellationToken ct)
        {
            List<ContentPublishEntity> rows = await _db.ContentPublishes.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(ct);

            return rows.Select(r => new AdminVersionEntry(
                r.Version, r.PreviousVersion, r.Kind.ToString(), r.PublishedByAdminId, r.Notes, r.CreatedAt)).ToList();
        }

        public AdminContentDiffResponse Diff(string fromVersion, string toVersion)
        {
            ContentPack from = GetPack(fromVersion);
            ContentPack to = GetPack(toVersion);

            Dictionary<string, CharacterData> fromById = from.Characters.ToDictionary(c => c.Id, StringComparer.Ordinal);
            Dictionary<string, CharacterData> toById = to.Characters.ToDictionary(c => c.Id, StringComparer.Ordinal);

            List<string> added = toById.Keys.Except(fromById.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> removed = fromById.Keys.Except(toById.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var changed = new List<AdminCharacterDiff>();

            foreach (string id in fromById.Keys.Intersect(toById.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                List<AdminFieldChange> fields = DiffCharacter(fromById[id], toById[id]);
                if (fields.Count > 0)
                {
                    changed.Add(new AdminCharacterDiff(id, fields));
                }
            }

            return new AdminContentDiffResponse(fromVersion, toVersion, added, removed, changed);
        }

        /// <summary>Resolves a version either from the in-memory registry (already loaded — the
        /// common case, since every version this process has ever published or rolled back to
        /// stays resident, same as <see cref="ContentPackRegistry"/> already does for
        /// player-facing content) or, if this process was restarted since, from its immutable
        /// snapshot under <c>content/_history/</c>.</summary>
        private ContentPack GetPack(string version)
        {
            if (_registry.LoadedVersions.Contains(version, StringComparer.Ordinal))
            {
                return _registry.Get(version);
            }

            string dir = Path.Combine(HistoryRoot, version);
            if (!Directory.Exists(dir))
            {
                throw new ContentVersionUnavailableException(version);
            }

            ContentPack pack = ContentPack.LoadFromDirectory(dir);
            _registry.Register(pack);
            return pack;
        }

        private void SnapshotIfMissing(string version)
        {
            string dest = Path.Combine(HistoryRoot, version);
            if (Directory.Exists(dest))
            {
                return;
            }

            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(_root))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
            }
        }

        /// <summary>Builds a full in-memory <see cref="ContentPack"/> — whichever type(s) were
        /// edited plus every other file read straight off disk — and validates it. This is the one
        /// path both <see cref="Validate"/> and <see cref="PublishAsync"/> go through, so a publish
        /// can never see a looser check than the validate button did.</summary>
        private ContentPack BuildStagedPack(
            List<CharacterData> characters,
            List<SkillDefinition> skills,
            List<EnemyData> enemies,
            List<EncounterData> encounters,
            ContentPack basePack,
            string overrideVersion)
        {
            string manifestJson = BuildManifestJson(basePack.Manifest, overrideVersion ?? basePack.Version);
            string charactersJson = characters != null ? SerializeNamedList("characters", characters) : null;
            string skillsJson = skills != null ? SerializeNamedList("skills", skills) : null;
            string enemiesJson = enemies != null ? SerializeNamedList("enemies", enemies) : null;
            string encountersJson = encounters != null ? SerializeNamedList("encounters", encounters) : null;

            return ContentPack.Load(fileName =>
            {
                if (fileName == "manifest.json")
                {
                    return manifestJson;
                }

                if (charactersJson != null && fileName == basePack.Manifest.Characters)
                {
                    return charactersJson;
                }

                if (skillsJson != null && fileName == basePack.Manifest.Skills)
                {
                    return skillsJson;
                }

                if (enemiesJson != null && fileName == basePack.Manifest.Enemies)
                {
                    return enemiesJson;
                }

                if (encountersJson != null && fileName == basePack.Manifest.Encounters)
                {
                    return encountersJson;
                }

                string path = Path.Combine(_root, fileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        /// <summary>Serializes one content type's edited list back into the <c>{ "&lt;name&gt;":
        /// [...] }</c> shape its file holds on disk (<c>ContentPack.Load</c>'s private
        /// <c>SkillFile</c>/<c>CharacterFile</c>/<c>EnemyFile</c>/<c>EncounterFile</c> wrappers).</summary>
        private static string SerializeNamedList<T>(string propertyName, List<T> items)
        {
            var wrapper = new Dictionary<string, object> { [propertyName] = items };
            return JsonConvert.SerializeObject(wrapper, ContentPack.SerializerSettings);
        }

        private static string BuildManifestJson(ContentManifest current, string version)
        {
            var manifest = new ContentManifest
            {
                ContentVersion = version,
                RulesVersion = current.RulesVersion,
                Skills = current.Skills,
                Characters = current.Characters,
                Enemies = current.Enemies,
                Progression = current.Progression,
                Encounters = current.Encounters,
                Stages = current.Stages
            };

            return JsonConvert.SerializeObject(manifest, ContentPack.SerializerSettings);
        }

        /// <summary>
        /// Bumps the patch component, then keeps bumping past any version this process still has
        /// on file — either in the live registry or as a history snapshot — before returning.
        /// <para>
        /// Bug this closes: a naive single bump (oldVersion's patch + 1) collides after a
        /// publish-then-rollback-then-publish-again sequence. Rollback moves
        /// <see cref="ContentPackRegistry.LatestVersion"/> <b>backward</b> to an older version; the
        /// very next publish then bumps forward from that older number and lands back on a version
        /// that was already registered by the publish that got rolled back — <c>Register</c> throws
        /// "already registered" (found via <c>AdminContentTests</c>, not by inspection). Skipping
        /// past every already-known version, not just the immediately following one, is what
        /// actually fixes it rather than just moving the collision one bump later.
        /// </para>
        /// </summary>
        private string NextUnusedVersion(string version)
        {
            string candidate = BumpPatch(version);
            while (_registry.LoadedVersions.Contains(candidate, StringComparer.Ordinal)
                || Directory.Exists(Path.Combine(HistoryRoot, candidate)))
            {
                candidate = BumpPatch(candidate);
            }

            return candidate;
        }

        private static string BumpPatch(string version)
        {
            string[] parts = version.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[2], out int patch))
            {
                throw new InvalidOperationException(
                    "contentVersion '" + version + "' is not in the major.minor.patch shape this tool bumps.");
            }

            return parts[0] + "." + parts[1] + "." + (patch + 1);
        }

        private static List<AdminFieldChange> DiffCharacter(CharacterData a, CharacterData b)
        {
            var fields = new List<AdminFieldChange>();
            JObject ja = JObject.FromObject(a, JsonSerializer.Create(ContentPack.SerializerSettings));
            JObject jb = JObject.FromObject(b, JsonSerializer.Create(ContentPack.SerializerSettings));
            DiffJson(string.Empty, ja, jb, fields);
            return fields;
        }

        private static void DiffJson(string path, JToken a, JToken b, List<AdminFieldChange> fields)
        {
            if (JToken.DeepEquals(a, b))
            {
                return;
            }

            if (a is JObject oa && b is JObject ob)
            {
                var keys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (JProperty p in oa.Properties())
                {
                    keys.Add(p.Name);
                }

                foreach (JProperty p in ob.Properties())
                {
                    keys.Add(p.Name);
                }

                foreach (string key in keys)
                {
                    JToken av = oa[key] ?? JValue.CreateNull();
                    JToken bv = ob[key] ?? JValue.CreateNull();
                    DiffJson(path.Length == 0 ? key : path + "." + key, av, bv, fields);
                }

                return;
            }

            fields.Add(new AdminFieldChange(path, a?.ToString() ?? "null", b?.ToString() ?? "null"));
        }
    }
}
