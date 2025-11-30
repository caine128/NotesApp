using NotesApp.Application;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Infrastructure;
using NotesApp.Worker;
using NotesApp.Worker.Configuration;
using NotesApp.Worker.Dispatching;
using NotesApp.Worker.Identity;
using NotesApp.Worker.Outbox;

var builder = Host.CreateApplicationBuilder(args);

// Bind OutboxWorkerOptions from configuration section "OutboxWorker"
builder.Services.AddOptions<OutboxWorkerOptions>()
    .Bind(builder.Configuration.GetSection(OutboxWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        o => o.MaxBatchSize > 0 && o.PollingIntervalMilliseconds > 0 && o.MaxRetryAttempts > 0,
        "OutboxWorker options must all be positive numbers.")
    .ValidateOnStart();

// Register your Application + Infrastructure layers just like in NotesApp.Api.
// If your actual extension methods have different names, swap these lines accordingly.
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);

// Remove any ICurrentUserService registrations from Infrastructure,
// because the worker must not depend on HttpContext-based CurrentUserService.
var currentUserDescriptors = builder.Services
    .Where(d => d.ServiceType == typeof(ICurrentUserService))
    .ToList();

foreach (var descriptor in currentUserDescriptors)
{
    builder.Services.Remove(descriptor);
}

// Register the Outbox processing context accessor as a singleton
builder.Services.AddSingleton<IOutboxProcessingContextAccessor, OutboxProcessingContextAccessor>();

// Override ICurrentUserService with the worker-specific implementation
builder.Services.AddSingleton<ICurrentUserService, WorkerCurrentUserService>();

// Register dispatcher implementation
builder.Services.AddScoped<IOutboxMessageDispatcher, LoggingOutboxMessageDispatcher>();

// Register the OutboxProcessingWorker as a hosted service
builder.Services.AddHostedService<OutboxProcessingWorker>();


var host = builder.Build();
host.Run();
