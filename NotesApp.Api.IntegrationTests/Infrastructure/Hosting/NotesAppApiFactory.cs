using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using NotesApp.Api.IntegrationTests.Infrastructure.Auth;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Hosting
{
    /// <summary>
    /// Custom WebApplicationFactory that:
    /// - Starts the real NotesApp.Api application (Program).
    /// - Replaces real authentication with TestAuthHandler for tests.
    /// - Exposes helpers to create HttpClient instances as specific fake users.
    /// </summary>
    public sealed class NotesAppApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // ConfigureTestServices is the recommended way to override services
            // just for the test host. We override authentication here.
            builder.ConfigureTestServices(services =>
            {
                // Remove any existing authentication configuration if needed
                // (usually not required, but safe to ensure our scheme becomes default).

                services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName,
                            options => { /* no special options needed */ });
            });
        }

        /// <summary>
        /// Creates an HttpClient that sends requests as the given "fake" user.
        /// The user id is propagated to the API via the X-Test-UserId header,
        /// which our TestAuthHandler reads to build the ClaimsPrincipal.
        /// </summary>
        public HttpClient CreateClientAsUser(Guid userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
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
