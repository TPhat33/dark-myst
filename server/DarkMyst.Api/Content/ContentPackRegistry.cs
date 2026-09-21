using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkMyst.Content;
using Microsoft.Extensions.Logging;

namespace DarkMyst.Api.Content
{
    /// <summary>
    /// Holds every content version the server currently needs resolvable at once, keyed by
    /// <c>ContentPack.Version</c>. docs/05-content-pipeline.md requires this: an in-flight
    /// expedition, and an owned character that has not been migrated yet, each pin the version
    /// they were created under, and both must keep working after a newer version becomes the one
    /// new grants use.
    /// <para>
    /// This round does not implement retiring an old version from memory (see
    /// docs/10-backend-spec.md "deliberately deferred") — every version ever loaded here stays
    /// resident for the process lifetime. That is safe at this scale and wrong at a much larger
    /// one; it is flagged, not hidden.
    /// </para>
    /// </summary>
    public sealed class ContentPackRegistry
    {
        private readonly ConcurrentDictionary<string, ContentPack> _packs = new ConcurrentDictionary<string, ContentPack>(StringComparer.Ordinal);
        private volatile string _latestVersion;
        private readonly object _latestLock = new object();

        public ContentPackRegistry(ILogger<ContentPackRegistry> logger)
        {
            Logger = logger;
        }

        private ILogger<ContentPackRegistry> Logger { get; }

        /// <summary>The version new character grants and new expedition runs are created under.</summary>
        public string LatestVersion => _latestVersion;

        public ContentPack Latest => Get(_latestVersion);

        /// <summary>Loads every immediate subdirectory of <paramref name="root"/> as its own
        /// content pack (subdirectory name is not trusted; <c>manifest.json</c>'s own
        /// <c>contentVersion</c> is what keys the registry). The subdirectory whose version sorts
        /// highest by <see cref="StringComparer.Ordinal"/> — semver strings compare correctly this
        /// way for the single-digit components this project uses — becomes <see cref="Latest"/>
        /// unless <paramref name="explicitLatest"/> overrides it.</summary>
        public static ContentPackRegistry LoadFromVersionsDirectory(
            string root, ILogger<ContentPackRegistry> logger, string explicitLatest = null)
        {
            var registry = new ContentPackRegistry(logger);

            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException("Content versions directory not found: " + root);
            }

            string[] versionDirs = Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal).ToArray();
            if (versionDirs.Length == 0)
            {
                throw new InvalidOperationException("Content versions directory has no version folders: " + root);
            }

            foreach (string dir in versionDirs)
            {
                ContentPack pack = ContentPack.LoadFromDirectory(dir);
                registry.Register(pack);
            }

            registry.SetLatest(explicitLatest ?? versionDirs.Select(d => Path.GetFileName(d)).OrderBy(v => v, StringComparer.Ordinal).Last());
            return registry;
        }

        /// <summary>
        /// Loads exactly one content directory (the shape <c>content/</c> has in this repo today —
        /// one manifest, no per-version subfolders) as the server's only, latest version. This is
        /// the production path until a real content-publishing workflow exists (docs/05's "เครื่องมือ
        /// เผยแพร่" is still ระยะ D future work); <see cref="LoadFromVersionsDirectory"/> is what a
        /// multi-version deployment, or a test that needs two versions loaded at once, uses instead.
        /// </summary>
        public static ContentPackRegistry LoadSingleDirectory(string directory, ILogger<ContentPackRegistry> logger)
        {
            var registry = new ContentPackRegistry(logger);
            registry.Register(ContentPack.LoadFromDirectory(directory));
            return registry;
        }

        public void Register(ContentPack pack)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (!_packs.TryAdd(pack.Version, pack))
            {
                throw new InvalidOperationException("Content version '" + pack.Version + "' is already registered.");
            }

            Logger?.LogInformation("Registered content version {Version}", pack.Version);

            lock (_latestLock)
            {
                if (_latestVersion == null)
                {
                    _latestVersion = pack.Version;
                }
            }
        }

        public void SetLatest(string version)
        {
            if (!_packs.ContainsKey(version))
            {
                throw new InvalidOperationException("Cannot make unregistered version '" + version + "' latest.");
            }

            _latestVersion = version;
        }

        /// <summary>Resolves a pinned version. Throws <see cref="ContentVersionUnavailableException"/>
        /// — never a raw KeyNotFoundException — so callers can turn this into a clean 409, not a 500,
        /// for the case docs/09-expedition-spec.md calls out: resuming a run whose content version has
        /// since been retired from this server entirely.</summary>
        public ContentPack Get(string version)
        {
            if (version != null && _packs.TryGetValue(version, out ContentPack pack))
            {
                return pack;
            }

            throw new ContentVersionUnavailableException(version);
        }

        public IReadOnlyCollection<string> LoadedVersions => _packs.Keys.ToArray();
    }

    /// <summary>Thrown by <see cref="ContentPackRegistry.Get"/> when a pinned content version is
    /// not (or no longer) resident. Distinguished from other content errors so the API layer can
    /// return a clean, specific refusal instead of a 500.</summary>
    public sealed class ContentVersionUnavailableException : Exception
    {
        public ContentVersionUnavailableException(string version)
            : base("Content version '" + version + "' is not available on this server.")
        {
            Version = version;
        }

        public string Version { get; }
    }
}
