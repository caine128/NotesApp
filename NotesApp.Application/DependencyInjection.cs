using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Application.Common.Behaviors;
using NotesApp.Application.Tasks.Commands.CreateTask;


namespace NotesApp.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services,
                                                                IConfiguration configuration)
        {
            var applicationAssembly = typeof(CreateTaskCommand).Assembly;

            // 1) MediatR – scan Application assembly for handlers
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(applicationAssembly);
            });

            // 2) FluentValidation – scan Application assembly for validators
            services.AddValidatorsFromAssembly(applicationAssembly);

            // 3) MediatR pipeline behaviors (ValidationBehavior, later maybe LoggingBehavior)
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            // 4) Application-level options (if/when we add them) can be configured here.
            //    e.g., services.AddOptions<CalendarOptions>()...

            return services;
        }
    }
}
