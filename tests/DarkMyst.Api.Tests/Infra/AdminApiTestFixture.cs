using System;
using System.IO;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// A fixture whose server reads/writes an isolated, throwaway copy of <c>content/</c>, not the
    /// real repo directory <see cref="ContentLocator"/> would otherwise resolve. Every admin
    /// content test that publishes or rolls back a version needs this — writing to the real
    /// <c>content/</c> during a test run would leave the working tree dirty (or actively break the
    /// real content pack) the moment the test suite touches disk.
    /// </summary>
    public sealed class AdminApiTestFixture : ApiTestFixture
    {
        private string _contentRoot;

        public string ContentRoot => _contentRoot;

        protected override Action<IServiceCollection> BuildConfigureServices()
        {
            string sourceDir = ContentLocator.Locate(new ConfigurationBuilder().Build());
            _contentRoot = Path.Combine(Path.GetTempPath(), "darkmyst-admin-content-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_contentRoot);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(_contentRoot, Path.GetFileName(file)));
            }

            string root = _contentRoot;
            return services =>
            {
                services.RemoveAll<ContentPackRegistry>();
                services.RemoveAll<ContentRootPath>();
                services.AddSingleton(new ContentRootPath(root));
                services.AddSingleton(sp => ContentPackRegistry.LoadSingleDirectory(
                    root, sp.GetRequiredService<ILogger<ContentPackRegistry>>()));
            };
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();

            if (_contentRoot != null && Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
    }
}
