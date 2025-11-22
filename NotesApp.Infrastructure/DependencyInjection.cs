using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
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
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 3) System clock (for time abstraction)
            services.AddSingleton<ISystemClock, SystemClock>();

            // TODO: add blob storage, caching, background workers, etc. here later.

            return services;
        }
    }
}
