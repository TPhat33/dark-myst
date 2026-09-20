using System;
using System.IO;
using DarkMyst.Content;

namespace DarkMyst.Content.Tests
{
    /// <summary>
    /// Loads the real <c>content/</c> directory once per test run. These tests are the gate
    /// that stops a broken content edit from reaching a build.
    /// </summary>
    internal static class ShippedContent
    {
        private static readonly Lazy<ContentPack> Pack = new Lazy<ContentPack>(
            () => ContentPack.LoadFromDirectory(Directory));

        public static ContentPack Instance => Pack.Value;

        public static string Directory => Path.Combine(FindRepositoryRoot(), "content");

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DarkMyst.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not find DarkMyst.sln above " + AppContext.BaseDirectory + ".");
        }
    }
}
