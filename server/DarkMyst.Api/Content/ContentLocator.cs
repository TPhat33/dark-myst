using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace DarkMyst.Api.Content
{
    /// <summary>
    /// Finds the repo's <c>content/</c> directory without assuming a particular process working
    /// directory. <c>dotnet run</c>, the test host (<c>WebApplicationFactory</c>, whose working
    /// directory is the test assembly's own bin folder) and a published app all start from
    /// different places, so this walks upward from <see cref="AppContext.BaseDirectory"/> looking
    /// for the one directory that actually has a <c>manifest.json</c> in it, rather than
    /// hardcoding a relative path that only one of those hosts would resolve correctly.
    /// </summary>
    public static class ContentLocator
    {
        public static string Locate(IConfiguration configuration)
        {
            string configured = configuration["Content:Directory"];
            if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "manifest.json")))
            {
                return Path.GetFullPath(configured);
            }

            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "content");
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                {
                    return candidate;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException(
                "Could not locate a content/ directory with a manifest.json above " + AppContext.BaseDirectory
                + " — set Content:Directory in configuration.");
        }
    }
}
