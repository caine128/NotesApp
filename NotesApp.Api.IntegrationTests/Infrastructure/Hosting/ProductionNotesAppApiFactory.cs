using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Hosting
{
    /// <summary>
    /// WebApplicationFactory that forces the environment to "Production".
    /// This ensures that dev-only services (like dev device provisioning)
    /// are not registered.
    /// </summary>
    public sealed class ProductionNotesAppApiFactory : NotesAppApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Force environment to Production
            builder.UseEnvironment("Production");
        }
    }
}
