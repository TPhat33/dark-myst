using System;
using System.IO;
using System.Threading.Tasks;
using DarkMyst.Api.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// A fixture whose server has two content versions resident at once — the only way to
    /// genuinely exercise docs/05-content-pipeline.md / docs/09-expedition-spec.md's "an in-flight
    /// run must finish under the version it began with" rule end-to-end: one run pinned to the
    /// original version, which stays resolvable even after a newer version becomes "latest".
    /// </summary>
    public sealed class MultiVersionApiTestFixture : ApiTestFixture
    {
        private string _contentRoot;

        protected override Action<IServiceCollection> BuildConfigureServices()
        {
            _contentRoot = ContentVersions.PrepareTwoVersions();
            string root = _contentRoot;

            return services =>
            {
                services.RemoveAll<ContentPackRegistry>();
                services.AddSingleton(sp => ContentPackRegistry.LoadFromVersionsDirectory(
                    root, sp.GetRequiredService<ILogger<ContentPackRegistry>>(), explicitLatest: ContentVersions.OriginalVersion));
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
