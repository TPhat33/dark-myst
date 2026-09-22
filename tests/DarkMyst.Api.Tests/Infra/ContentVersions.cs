using System;
using System.IO;
using DarkMyst.Api.Content;
using Microsoft.Extensions.Configuration;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>Builds a two-version content directory tree (both copied from the repo's real
    /// <c>content/</c>, the second with its <c>contentVersion</c> bumped) for the one test that
    /// needs the server to have more than one version resident at once — see
    /// <see cref="MultiVersionApiTestFixture"/>.</summary>
    public static class ContentVersions
    {
        public const string OriginalVersion = "0.4.0";
        public const string BumpedVersion = "0.4.1";

        public static string PrepareTwoVersions()
        {
            string sourceDir = ContentLocator.Locate(new ConfigurationBuilder().Build());

            string root = Path.Combine(Path.GetTempPath(), "darkmyst-content-versions-" + Guid.NewGuid().ToString("n"));
            CopyDirectory(sourceDir, Path.Combine(root, OriginalVersion));
            CopyDirectory(sourceDir, Path.Combine(root, BumpedVersion));

            string manifestPath = Path.Combine(root, BumpedVersion, "manifest.json");
            string manifest = File.ReadAllText(manifestPath);
            File.WriteAllText(manifestPath, manifest.Replace(
                "\"contentVersion\": \"" + OriginalVersion + "\"",
                "\"contentVersion\": \"" + BumpedVersion + "\""));

            return root;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }
        }
    }
}
