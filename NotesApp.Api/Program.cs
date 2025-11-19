using MediatR;
using NotesApp.Application.Tasks.Commands.CreateTask;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Api.Infrastructure.Errors;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// --- MediatR ---
builder.Services.AddMediatR(cfg =>
{
    // Register all handlers from Application assembly
    cfg.RegisterServicesFromAssembly(typeof(CreateTaskCommand).Assembly);
});

// --- FluentValidation: scan Application assembly for validators ---
builder.Services.AddValidatorsFromAssembly(typeof(CreateTaskCommand).Assembly);

// --- Register ValidationBehavior as a pipeline behavior ---
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ProblemDetails: standardized error responses (RFC 9457 / 7807)
builder.Services.AddProblemDetails(options =>
{
    // Add useful default metadata (trace-id, instance, etc.)
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["trace-id"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["instance"] = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Return a ProblemDetails body for error status codes without content
app.UseStatusCodePages();

// Convert unhandled exceptions into ProblemDetails using the registered IExceptionHandler(s)
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// usual middleware
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
