---
name: aspnetcore-jwt-entra
description: ASP.NET Core JWT Bearer authentication with Microsoft Entra ID (formerly Azure AD). Covers AddAuthentication/AddJwtBearer configuration, TokenValidationParameters, AuthOptions strongly-typed config, FallbackPolicy, scope-based authorization policies, ICurrentUserService claims-reading pattern, middleware order, TestAuthHandler integration test pattern, and anti-patterns. Grounded in the actual patterns used in this codebase.
invocable: false
---

# ASP.NET Core JWT Bearer + Entra ID

## When to Use This Skill

Use this skill when:
- Configuring `AddAuthentication` / `AddJwtBearer` (new endpoint or revisiting existing setup)
- Adding or modifying authorization policies (scope checks, role checks)
- Reading claims from the current user (`ICurrentUserService`, `IHttpContextAccessor`)
- Changing `AuthOptions` configuration (`Authority`, `Audience`, `ValidIssuers`)
- Writing integration tests that need an authenticated user (`TestAuthHandler`)
- Reviewing auth middleware order or `ProblemDetails` error responses for 401/403

See [advanced-patterns.md](advanced-patterns.md) for dev/prod dual-scheme, `JwtBearerEvents` diagnostics, and the `CurrentUserService` account-linking pattern.

---

## Core Principles

1. **`MapInboundClaims = false`** — always set this. Without it, ASP.NET Core maps `sub` → `ClaimTypes.NameIdentifier` silently, which hides the actual Entra claim names and causes bugs.
2. **Validate everything** — `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true` in production.
3. **Reduce `ClockSkew`** — the default 5-minute tolerance is excessive when servers are NTP-synced. Use 1 minute.
4. **Strongly-typed `AuthOptions`** with `[Required]`/`[Url]` DataAnnotations and `ValidateOnStart()` — fail fast at startup if config is wrong.
5. **`FallbackPolicy = RequireAuthenticatedUser()`** — every endpoint requires auth by default; anonymous endpoints are opted in explicitly with `.AllowAnonymous()`.
6. **Claims come from `ICurrentUserService`** — handlers never read `HttpContext` or `ClaimsPrincipal` directly; those live in Infrastructure.
7. **`UseAuthentication()` before `UseAuthorization()`** — order is mandatory; reversing them silently breaks all authorization.

---

## Strongly-Typed AuthOptions

```csharp
public sealed class AuthOptions
{
    [Required, Url]
    public string Authority { get; init; } = default!;

    [Required]
    public string Audience { get; init; } = default!;

    // Optional: support multiple issuers (e.g., multi-tenant, CIAM + workforce)
    public string[]? ValidIssuers { get; init; }

    [Range(0, 10)]
    public int ClockSkewMinutes { get; init; } = 1;

    public string NameClaimType { get; init; } = "name";
    public string RoleClaimType  { get; init; } = "roles";
}
```

**appsettings.json**
```json
{
  "Authentication": {
    "Bearer": {
      "Authority": "https://{tenant}.ciamlogin.com/{tenant-id}/v2.0",
      "Audience":  "api://{your-api-client-id}"
    }
  }
}
```

**Registration with fail-fast validation**
```csharp
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection("Authentication:Bearer"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## AddAuthentication + AddJwtBearer

```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

// Configure options using DI so AuthOptions are resolved after being bound
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>, IWebHostEnvironment>(
        (jwtOptions, authAccessor, env) =>
        {
            var auth = authAccessor.Value;

            jwtOptions.Authority          = auth.Authority;
            jwtOptions.Audience           = auth.Audience;
            jwtOptions.MapInboundClaims   = false; // ALWAYS false with Entra

            jwtOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuers             = auth.ValidIssuers is { Length: > 0 }
                                            ? auth.ValidIssuers
                                            : new[] { auth.Authority },

                ValidateAudience         = true,
                ValidAudience            = auth.Audience,

                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.FromMinutes(auth.ClockSkewMinutes),

                ValidateIssuerSigningKey = true,

                NameClaimType = auth.NameClaimType,
                RoleClaimType = auth.RoleClaimType
            };

            // Disable HTTPS metadata requirement only in dev
            jwtOptions.RequireHttpsMetadata = !env.IsDevelopment();
        });
```

---

## Authorization: FallbackPolicy + Scope Policies

```csharp
builder.Services.AddAuthorization(options =>
{
    // Every endpoint requires auth unless it calls .AllowAnonymous()
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Scope check — reads the Entra v2 "scp" claim (space-separated)
    options.AddPolicy("ApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
        {
            var scp = ctx.User.FindFirst("scp")?.Value;
            if (string.IsNullOrWhiteSpace(scp)) return false;
            var scopes = scp.Split(' ');
            return scopes.Contains("notes.readwrite") ||
                   scopes.Contains("api://your-client-id/notes.readwrite");
        });
    });
});
```

Apply the scope policy on a controller or action:
```csharp
[Authorize(Policy = "ApiScope")]
[ApiController]
[Route("api/notes")]
public sealed class NotesController : ControllerBase { ... }
```

Anonymous endpoints (OpenAPI docs, health checks):
```csharp
app.MapOpenApi().AllowAnonymous();
```

---

## Middleware Order

```csharp
app.UseHttpsRedirection();
app.UseAuthentication();  // must come before UseAuthorization
app.UseAuthorization();
app.MapControllers();
```

**ProblemDetails for 401/403** — add before middleware:
```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["trace-id"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["instance"]  =
            $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// In pipeline:
app.UseStatusCodePages();   // adds ProblemDetails body for 401/403 without content
app.UseExceptionHandler();  // converts unhandled exceptions
```

---

## ICurrentUserService

The Application layer never touches `HttpContext` or `ClaimsPrincipal` directly. Use `ICurrentUserService`:

```csharp
// Application layer — interface
public interface ICurrentUserService
{
    Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default);
}
```

**In a handler:**
```csharp
var userId = await _currentUser.GetUserIdAsync(cancellationToken);
```

**Claims-reading priority in the Infrastructure implementation:**
```csharp
// User identity — Entra uses 'oid'; fall back to 'sub' then NameIdentifier
var externalId =
    principal.FindFirst("oid")?.Value ??
    principal.FindFirst("sub")?.Value ??
    principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

// Provider/issuer — 'iss' preferred; fall back to 'tid'
var provider =
    principal.FindFirst("iss")?.Value ??
    principal.FindFirst("tid")?.Value ??
    "UnknownIssuer";

// Email — Entra often puts it in 'preferred_username' not ClaimTypes.Email
var email =
    principal.FindFirst(ClaimTypes.Email)?.Value ??
    principal.FindFirst("email")?.Value ??
    principal.FindFirst("preferred_username")?.Value;
```

---

## Testing: TestAuthHandler

Replace real JWT in integration tests with a custom `AuthenticationHandler` that reads from headers:

```csharp
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName       = "TestAuth";
    public const string UserIdHeaderName = "X-Test-UserId";
    public const string ScopeHeaderName  = "X-Test-Scopes";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValue = Context.Request.Headers[UserIdHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue) || !Guid.TryParse(headerValue, out var userId))
            return Task.FromResult(AuthenticateResult.NoResult()); // → 401

        var scopeValue = Context.Request.Headers[ScopeHeaderName].FirstOrDefault();

        var claims = new List<Claim>
        {
            new("oid",                     userId.ToString()),
            new("sub",                     userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name,           "Integration Test User"),
            new("iss",                     "https://test.local"),
        };
        if (!string.IsNullOrWhiteSpace(scopeValue))
            claims.Add(new Claim("scp", scopeValue));

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

**WebApplicationFactory wiring:**
```csharp
public class NotesAppApiFactory : WebApplicationFactory<Program>
{
    private const string RequiredScope = "api://your-client-id/notes.readwrite";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateClientAsUser(Guid userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeaderName, RequiredScope);
        return client;
    }
}
```

---

## Anti-Patterns

```csharp
// DON'T: Leave MapInboundClaims at its default (true)
// Silently maps 'sub' → ClaimTypes.NameIdentifier, hiding Entra claim names
jwtOptions.MapInboundClaims = true; // never

// DON'T: Skip audience validation
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateAudience = false // any token for any audience is accepted
};

// DON'T: Forget to reduce ClockSkew
// Default is 5 minutes — tokens appear valid 5 minutes after they expire
ClockSkew = TimeSpan.Zero; // too aggressive; use TimeSpan.FromMinutes(1)

// DON'T: Read HttpContext or claims inside Application handlers
public async Task<Result<NoteDetailDto>> Handle(GetNoteQuery request, CancellationToken ct)
{
    var userId = _httpContextAccessor.HttpContext!.User.FindFirst("oid")!.Value; // wrong layer
    // use: await _currentUser.GetUserIdAsync(ct)
}

// DON'T: Skip FallbackPolicy
// Without it, unauthenticated requests reach endpoints that forgot [Authorize]

// DON'T: UseAuthorization before UseAuthentication
app.UseAuthorization();   // wrong order
app.UseAuthentication();  // silently allows requests through without identity

// DON'T: Validate the 'scp' claim for app-only (client_credentials) tokens
// App-only tokens use 'roles' claim, not 'scp' — different flow entirely
```

---

## Resources

- **ASP.NET Core JWT Bearer docs**: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-bearer
- **TokenValidationParameters**: https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters
- **Entra ID token claims reference**: https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference
- **AddAuthorization / policy-based auth**: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies
- **ProblemDetails (RFC 9457)**: https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors
