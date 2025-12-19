using FluentResults.Extensions.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NotesApp.Api.Configuration;
using NotesApp.Api.DeviceProvisioning;
using NotesApp.Api.FluentResults;
using NotesApp.Api.Infrastructure.Errors;
using NotesApp.Api.Infrastructure.OpenApi;
using NotesApp.Application;
using NotesApp.Infrastructure;
using NotesApp.Infrastructure.Auth;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

// 1) Application layer DI
builder.Services.AddApplicationServices(builder.Configuration);

// 2) Infrastructure layer DI
builder.Services.AddInfrastructureServices(builder.Configuration);

// 3) Bind AuthOptions (Options pattern + validation)
var authSection = builder.Configuration.GetSection("Authentication:Bearer");

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(authSection)
    .ValidateDataAnnotations()
    .ValidateOnStart(); // fail fast at startup if config is invalid

// 4a) Dev environment: support both Debug header and real JWT
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IDeviceDebugProvisioningService, DevDeviceDebugProvisioningService>();

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = "DevOrJwt";
            options.DefaultChallengeScheme = "DevOrJwt";
        })
        .AddJwtBearer() // actual JWT bearer, configured below
        .AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
            DebugAuthenticationHandler.SchemeName, options => { })
        .AddPolicyScheme("DevOrJwt", "Debug or Jwt", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                // If the request has the debug header, use Debug auth
                if (context.Request.Headers.ContainsKey(DebugAuthConstants.DebugUserHeaderName))
                {
                    return DebugAuthenticationHandler.SchemeName;
                }

                // Otherwise, fall back to standard JwtBearer
                return JwtBearerDefaults.AuthenticationScheme;
            };
        });
}
else
{
    builder.Services.AddScoped<IDeviceDebugProvisioningService, NoOpDeviceDebugProvisioningService>();

    // Production / non-dev: JWT only, no debug bypass
    builder.Services
    .AddAuthentication(options =>
    {
        // Default to JWT in general
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();
}



// 4b) Configure JwtBearerOptions using DI (AuthOptions + environment)
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>, IWebHostEnvironment>(
        (options, authOptionsAccessor, env) =>
        {
            var authOptions = authOptionsAccessor.Value;

            // Authority & Audience from your strongly-typed config
            options.Authority = authOptions.Authority;
            options.Audience = authOptions.Audience;
            options.MapInboundClaims = false;
            // Validate issuer, audience, lifetime, signature, etc.
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                // Use explicit list if provided, otherwise fall back to Authority
                ValidIssuers = authOptions.ValidIssuers is { Length: > 0 }
                    ? authOptions.ValidIssuers
                    : new[] { authOptions.Authority },

                ValidateAudience = true,
                ValidAudience = authOptions.Audience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(authOptions.ClockSkewMinutes),

                ValidateIssuerSigningKey = true,

                // Claim mapping
                NameClaimType = authOptions.NameClaimType,
                RoleClaimType = authOptions.RoleClaimType
            };

            // For dev you *can* disable HTTPS metadata; for production this should be true.
            options.RequireHttpsMetadata = !env.IsDevelopment();

            // Add event handlers for detailed authentication diagnostics
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.WriteLine($"❌ JWT Authentication Failed: {context.Exception.Message}");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Console.WriteLine($"✅ JWT Token Validated for user: {context.Principal?.Identity?.Name ?? "Unknown"}");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    Console.WriteLine($"⚠️  JWT Challenge: {context.Error} - {context.ErrorDescription}");
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    Console.WriteLine($"📩 JWT Message Received");
                    return Task.CompletedTask;
                }
            };
        });


// 5) Add authorization services as usual
builder.Services.AddAuthorization(options =>
{
    // Require an authenticated user for all endpoints by default
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Require our custom API scope for normal API access
    options.AddPolicy("ApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
        {
            // Entra v2 tokens use "scp" (space-separated scopes)
            var scopeClaim = ctx.User.FindFirst("scp")?.Value;
            if (string.IsNullOrWhiteSpace(scopeClaim))
                return false; // <-- no scope => policy fails => 403, not 500

            // TODO: if you want, move this string to configuration
            //// Entra External ID sends scope as just "notes.readwrite"
            // (without the api:// prefix)
            var scopes = scopeClaim.Split(' ');
            return scopes.Contains("notes.readwrite") ||
                   scopes.Contains("api://d1047ffd-a054-4a9f-aeb0-198996f0c0c6/notes.readwrite");
        });
    });
});

// 6) Controllers + ProblemDetails + Exception handling
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

// Required for OpenAPI to discover controller endpoints (not just Minimal APIs)
builder.Services.AddEndpointsApiExplorer();

// OpenAPI document generation with security schemes for Scalar UI
builder.Services.AddOpenApi("v1", options =>
{
    // Add security scheme definitions so Scalar can display auth options
    options.AddDocumentTransformer<SecuritySchemeTransformer>();
});

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
    // ═══════════════════════════════════════════════════════════════════════════════
    // DEVELOPMENT ONLY: Allow anonymous access to API documentation
    // The FallbackPolicy (RequireAuthenticatedUser) would otherwise block these.
    // This is safe because these endpoints are only mapped in Development.
    // ═══════════════════════════════════════════════════════════════════════════════

    // OpenAPI JSON spec at /openapi/v1.json
    app.MapOpenApi().AllowAnonymous();


    // Scalar interactive UI at /scalar/v1
    // To authenticate when testing endpoints FROM Scalar:
    //   1. Click the "Auth" button in Scalar UI
    //   2. Select "Debug" scheme - it's pre-filled with "dev-user"
    //   OR
    //   3. Select "Bearer" scheme and paste a real JWT token
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("NotesApp API")
            .WithTheme(ScalarTheme.Moon)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            // Use new 2.x API: set Debug as preferred and pre-fill the value
            .AddPreferredSecuritySchemes("Debug")
            .AddApiKeyAuthentication("Debug", apiKey =>
            {
                apiKey.Value = "dev-user";
            });
    }).AllowAnonymous();
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
