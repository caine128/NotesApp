using FluentResults.Extensions.AspNetCore;
using NotesApp.Api.FluentResults;
using NotesApp.Api.Infrastructure.Errors;
using NotesApp.Application;              
using NotesApp.Infrastructure;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

// 1) Application layer DI
builder.Services.AddApplicationServices(builder.Configuration);

// 2) Infrastructure layer DI
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3) Controllers + ProblemDetails + Exception handling
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

// Configure FluentResults ↔ ASP.NET Core mapping
AspNetCoreResult.Setup(config =>
{
    config.DefaultProfile = new NotesAppResultEndpointProfile();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Scalar interactive UI at /scalar
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("NotesApp API")
            .WithTheme(ScalarTheme.Moon); // optional, just looks nice
    });
}

// usual middleware
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();


app.MapGet("/", () => Results.Text(
    "NotesApp API is running. " +
    "Try /openapi/v1.json for the OpenAPI document " +
    "or /api/Tasks for task endpoints."));

app.MapControllers();
app.Run();
