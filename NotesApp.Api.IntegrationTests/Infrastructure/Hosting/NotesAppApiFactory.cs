using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Auth;
using NotesApp.Api.IntegrationTests.Infrastructure.Storage;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Infrastructure.Persistence;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Hosting
{
    /// <summary>
    /// Custom WebApplicationFactory that:
    /// - Starts the real NotesApp.Api application (Program).
    /// - Redirects the DB connection to a dedicated LocalDB database
    ///   (NotesApp_IntegrationTests) provisioned via EnsureCreated, keeping
    ///   the integration test database separate from the unit-test database
    ///   (NotesApp_Tests) and from any developer user-secrets database.
    /// - Replaces real authentication with TestAuthHandler for tests.
    /// - Replaces real Azure blob storage with FakeBlobStorageService for tests.
    /// - Exposes helpers to create HttpClient instances as specific fake users.
    ///
    /// Subclasses can override UseFakeBlobStorage to opt into the real Azure
    /// blob storage instead (see AzureNotesAppApiFactory).
    /// </summary>
    public class NotesAppApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private const string RequiredScope =
            "api://d1047ffd-a054-4a9f-aeb0-198996f0c0c6/notes.readwrite";

        // Dedicated LocalDB database for integration tests — separate from
        // NotesApp_Tests (used by Application.Tests) and any dev user-secrets DB.
        private const string IntegrationTestConnectionString =
            "Server=(localdb)\\MSSQLLocalDB;" +
            "Database=NotesApp_IntegrationTests;" +
            "Trusted_Connection=True;" +
            "MultipleActiveResultSets=true;" +
            "TrustServerCertificate=True;";

        // REFACTORED: Extracted as a virtual property so subclasses can opt out
        // of the fake and use the real AzureBlobStorageService instead.
        protected virtual bool UseFakeBlobStorage => true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Redirect to the integration-test-specific database so Application.Tests
            // (which call EnsureDeleted+EnsureCreated on NotesApp_Tests) never
            // interfere with this database, and so dotnet-ef database update against
            // the dev user-secrets DB doesn't affect test runs.
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = IntegrationTestConnectionString
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Always replace authentication with the test scheme.
                services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName,
                            options => { });

                // REFACTORED: Only substitute the fake when the subclass hasn't
                // opted into real Azure storage. This allows AzureNotesAppApiFactory
                // to use the real AzureBlobStorageService without duplicating
                // the auth setup.
                if (UseFakeBlobStorage)
                {
                    var blobDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IBlobStorageService));
                    if (blobDescriptor != null)
                        services.Remove(blobDescriptor);

                    services.AddSingleton<IBlobStorageService, FakeBlobStorageService>();
                }
            });
        }

        // Drop and recreate the integration test database before each test class
        // so the schema always reflects the current EF model (including any new
        // tables added since the last migration was generated).
        public async Task InitializeAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

        /// <summary>
        /// Creates an HttpClient that sends requests as the given "fake" user.
        /// The user id is propagated to the API via the X-Test-UserId header,
        /// which our TestAuthHandler reads to build the ClaimsPrincipal.
        /// </summary>
        public HttpClient CreateClientAsUser(Guid userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeaderName, RequiredScope);
            return client;
        }

        /// <summary>
        /// Creates an HttpClient as the default fake user (no explicit header).
        /// </summary>
        public HttpClient CreateClientAsDefaultUser()
        {
            return CreateClient();
        }
    }
}