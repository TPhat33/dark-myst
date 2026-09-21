using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// Boots the real <c>Program.cs</c> host in-process against a throwaway database — the same
    /// code path <c>dotnet run</c> uses, including startup migrations, exception mapping and the
    /// debug-grant endpoints (forced on here regardless of environment, since tests need a way to
    /// seed an account without a shop/gacha system — see server/DarkMyst.Api/Debug/DebugGrants.cs).
    /// </summary>
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly Action<IServiceCollection> _configureServices;

        public ApiFactory(string connectionString, Action<IServiceCollection> configureServices = null)
        {
            _connectionString = connectionString;
            _configureServices = configureServices;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // UseSetting, not ConfigureAppConfiguration: it writes into the settings dictionary
            // WebApplicationBuilder reads *while* WebApplication.CreateBuilder(args) itself is
            // still assembling configuration (Program.cs reads ConnectionStrings:ApiDb from that
            // same builder.Configuration a few lines later) — an additional config *source*
            // registered the ConfigureAppConfiguration way is layered in too late for a minimal
            // top-level Program.cs that reads its own configuration before calling Build().
            builder.UseSetting("ConnectionStrings:ApiDb", _connectionString);
            builder.UseSetting("Debug:AllowGrants", "true");

            if (_configureServices != null)
            {
                builder.ConfigureTestServices(_configureServices);
            }
        }
    }
}
