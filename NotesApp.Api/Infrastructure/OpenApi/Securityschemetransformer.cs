using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;


namespace NotesApp.Api.Infrastructure.OpenApi
{
    /// <summary>
    /// Adds security scheme definitions to the OpenAPI document.
    /// This allows Scalar (and other OpenAPI UIs) to display authentication options.
    /// 
    /// Defines two schemes:
    /// 1. "Debug" - Header-based auth using X-Debug-User (Development only)
    /// 2. "Bearer" - Standard JWT Bearer authentication
    /// 
    /// Based on official .NET 10 documentation:
    /// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi
    /// </summary>
    public sealed class SecuritySchemeTransformer(
        IAuthenticationSchemeProvider authenticationSchemeProvider,
        IWebHostEnvironment environment) : IOpenApiDocumentTransformer
    {
        public async Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            var authSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();

            // Build security schemes dictionary
            var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>();

            // In Development, add the X-Debug-User header scheme
            if (environment.IsDevelopment())
            {
                securitySchemes["Debug"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = "X-Debug-User",
                    In = ParameterLocation.Header,
                    Description = "Development only: Enter any identifier (e.g., 'dev-user'). " +
                                  "This bypasses Entra authentication for local testing."
                };
            }

            // Always define JWT Bearer scheme (used in production, optional in dev)
            if (authSchemes.Any(s => s.Name == "Bearer" || s.Name == "DevOrJwt"))
            {
                securitySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Enter your JWT access token from Microsoft Entra ID."
                };
            }

            // Assign to document components
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = securitySchemes;

            // Apply security requirements to all operations
            foreach (var pathItem in document.Paths.Values)
            {
                if (pathItem is null) continue;

                foreach (var operation in pathItem.Operations)
                {
                    operation.Value.Security ??= [];

                    // Add Debug scheme requirement (Development only)
                    if (securitySchemes.ContainsKey("Debug"))
                    {
                        operation.Value.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("Debug", document)] = []
                        });
                    }

                    // Add Bearer scheme requirement
                    if (securitySchemes.ContainsKey("Bearer"))
                    {
                        operation.Value.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                        });
                    }
                }
            }
        }
    }
}
