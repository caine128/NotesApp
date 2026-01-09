using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Infrastructure.Identity;
using NotesApp.Infrastructure.Notifications;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Storage;
using NotesApp.Infrastructure.Time;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
                                                                   IConfiguration configuration)
        {
            // 1) DbContext
            var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    // If you have migrations in Infrastructure:
                    // sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                    // Connection resiliency
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                });
            });

            // 2) Repositories + UnitOfWork
            services.AddScoped<ITaskRepository, TaskRepository>();
            services.AddScoped<INoteRepository, NoteRepository>();
            services.AddScoped<IBlockRepository, BlockRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IUserDeviceRepository, UserDeviceRepository>();
            services.AddScoped<IPushNotificationService, LoggingPushNotificationService>();

            // 3) System clock (for time abstraction)
            services.AddSingleton<ISystemClock, SystemClock>();


            services.AddScoped<ICurrentUserService, CurrentUserService>();


            // 4) Azure Blob Storage
            // Uses DefaultAzureCredential for authentication:
            // - In Azure: Managed Identity (no secrets required)
            // - Locally: Azure CLI, Visual Studio, or other developer credentials
            var blobServiceUri = configuration["Azure:Storage:Blob:ServiceUri"];
            if (!string.IsNullOrEmpty(blobServiceUri))
            {
                services.AddAzureClients(azure =>
                {
                    azure.AddBlobServiceClient(new Uri(blobServiceUri));

                    // Configure retry policy for transient failures
                    azure.ConfigureDefaults(options =>
                    {
                        options.Retry.MaxRetries = 3;
                        options.Retry.Mode = RetryMode.Exponential;
                        options.Retry.Delay = TimeSpan.FromSeconds(1);
                        options.Retry.MaxDelay = TimeSpan.FromSeconds(30);
                        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(100);
                    });

                    // DefaultAzureCredential automatically uses:
                    // 1. Environment variables (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET)
                    // 2. Managed Identity (when running in Azure)
                    // 3. Visual Studio credentials
                    // 4. Azure CLI credentials (az login)
                    // 5. Azure PowerShell credentials
                    azure.UseCredential(new DefaultAzureCredential());
                });

                services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
            }

            return services;
        }
    }
}
