using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.DeviceProvisioning;
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

            builder.ConfigureServices(services =>
            {
                // Replace whatever implementation is registered for IDeviceDebugProvisioningService
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IDeviceDebugProvisioningService));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<IDeviceDebugProvisioningService, TestNoOpDeviceDebugProvisioningService>();
            });
        }

        // Test-only no-op implementation: just pass the device id through
        private sealed class TestNoOpDeviceDebugProvisioningService : IDeviceDebugProvisioningService
        {
            public Task<Guid?> EnsureDeviceIdAsync(Guid? deviceId, CancellationToken cancellationToken)
                => Task.FromResult(deviceId);
        }
    }
}
