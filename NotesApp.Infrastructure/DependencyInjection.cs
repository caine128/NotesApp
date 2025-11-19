using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
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
            // TODO: when ready, configure your DbContext here:
            // var connectionString = configuration.GetConnectionString("DefaultConnection");
            // services.AddDbContext<AppDbContext>(options =>
            //     options.UseSqlServer(connectionString));

            // Repositories + UnitOfWork
            //services.AddScoped<ITaskRepository, TaskRepository>();
            //services.AddScoped<IUnitOfWork, UnitOfWork>();

            // System clock implementation (for ISystemClock)
            //services.AddSingleton<ISystemClock, SystemClock>();

            // TODO: add blob storage, caching, background workers, etc. here later.

            return services;
        }
    }
}
