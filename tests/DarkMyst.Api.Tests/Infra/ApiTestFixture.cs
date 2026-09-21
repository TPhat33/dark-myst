using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DarkMyst.Api.Tests.Infra
{
    /// <summary>
    /// One throwaway database, one in-process host, one <see cref="HttpClient"/> — shared across
    /// every test method in a class via <c>IClassFixture&lt;ApiTestFixture&gt;</c>, torn down (and
    /// the database dropped) once the class finishes. Different test classes never share a
    /// fixture instance, so they never share a database either.
    /// </summary>
    public class ApiTestFixture : IAsyncLifetime
    {
        private string _connectionString;

        public HttpClient Client { get; private set; }

        public ApiFactory Factory { get; private set; }

        public string ConnectionString => _connectionString;

        /// <summary>Override to react to service registration (e.g. swap in a multi-version
        /// content registry) before the host is built. Called synchronously right before the
        /// host is created, so a subclass doing file setup can stash whatever it needs to clean
        /// up later in its own fields.</summary>
        protected virtual Action<IServiceCollection> BuildConfigureServices()
        {
            return null;
        }

        public virtual async Task InitializeAsync()
        {
            _connectionString = await PostgresTestDatabase.CreateAsync();
            Factory = new ApiFactory(_connectionString, BuildConfigureServices());
            Client = Factory.CreateClient();
        }

        public virtual async Task DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            await PostgresTestDatabase.DropAsync(_connectionString);
        }
    }
}
