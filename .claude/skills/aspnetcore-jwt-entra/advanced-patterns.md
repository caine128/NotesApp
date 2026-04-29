# ASP.NET Core JWT + Entra — Advanced Patterns

## Contents

- [Dev/Prod Dual-Scheme Authentication](#devprod-dual-scheme-authentication)
- [JwtBearerEvents Diagnostics](#jwtbearerevents-diagnostics)
- [Multi-Issuer Configuration](#multi-issuer-configuration)
- [Entra External ID (CIAM) vs. Workforce Tenant](#entra-external-id-ciam-vs-workforce-tenant)
- [CurrentUserService: Account Linking and Race-Condition Handling](#currentuserservice-account-linking-and-race-condition-handling)

---

## Dev/Prod Dual-Scheme Authentication

In development, it's useful to bypass real JWT so you can test without valid Entra tokens. The `PolicyScheme` forwarder chooses which underlying scheme to use based on a header:

```csharp
if (env.IsDevelopment())
{
    services
        .AddAuthentication(options =>
        {
            options.DefaultScheme          = "DevOrJwt";
            options.DefaultChallengeScheme = "DevOrJwt";
        })
        .AddJwtBearer()   // real JWT — configured separately via IOptions<JwtBearerOptions>
        .AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
            DebugAuthenticationHandler.SchemeName, _ => { })
        .AddPolicyScheme("DevOrJwt", "Debug or JWT", options =>
        {
            options.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(DebugAuthConstants.DebugUserHeaderName)
                    ? DebugAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme;
        });
}
else
{
    services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer();
}
```

**DebugAuthenticationHandler** — dev-only, accepts a well-known header value instead of a token:

```csharp
internal sealed class DebugAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Debug";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValue = Context.Request.Headers[DebugAuthConstants.DebugUserHeaderName]
                                          .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim("oid", headerValue),
            new Claim("sub", headerValue),
            new Claim(ClaimTypes.NameIdentifier, headerValue),
            new Claim("scp", "notes.readwrite"),
        };
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

> **Security rule**: register `DebugAuthenticationHandler` only when `env.IsDevelopment()`. Never ship it to production — it bypasses all real authentication.

---

## JwtBearerEvents Diagnostics

`JwtBearerEvents` hooks let you trace every stage of token validation. Useful during initial Entra setup when it's unclear why tokens are being rejected:

```csharp
jwtOptions.Events = new JwtBearerEvents
{
    OnAuthenticationFailed = context =>
    {
        // Logs the reason the token was rejected (expired, wrong audience, etc.)
        logger.LogWarning("JWT auth failed: {Message}", context.Exception.Message);
        return Task.CompletedTask;
    },
    OnTokenValidated = context =>
    {
        var name = context.Principal?.FindFirst("preferred_username")?.Value ?? "unknown";
        logger.LogDebug("JWT validated for {User}", name);
        return Task.CompletedTask;
    },
    OnChallenge = context =>
    {
        // Fires when the handler sends a 401 challenge
        logger.LogDebug("JWT challenge: {Error} — {Description}", context.Error, context.ErrorDescription);
        return Task.CompletedTask;
    },
    OnForbidden = context =>
    {
        // Fires when a valid token fails an authorization policy (→ 403)
        logger.LogWarning("JWT forbidden for {User}", context.Principal?.Identity?.Name);
        return Task.CompletedTask;
    }
};
```

> **Remove or downgrade these to `Debug` in production.** `OnAuthenticationFailed` can expose exception details in logs; gate it on log level.

---

## Multi-Issuer Configuration

When you need to accept tokens from more than one Entra tenant or from both a CIAM tenant and a workforce tenant:

```csharp
// appsettings.json
{
  "Authentication": {
    "Bearer": {
      "Authority": "https://login.microsoftonline.com/common/v2.0",
      "Audience":  "api://your-client-id",
      "ValidIssuers": [
        "https://login.microsoftonline.com/{tenant-a-id}/v2.0",
        "https://login.microsoftonline.com/{tenant-b-id}/v2.0",
        "https://your-tenant.ciamlogin.com/{ciam-tenant-id}/v2.0"
      ]
    }
  }
}
```

```csharp
// In JwtBearerOptions configuration:
ValidIssuers = auth.ValidIssuers is { Length: > 0 }
    ? auth.ValidIssuers
    : new[] { auth.Authority },
```

> Never set `ValidateIssuer = false` as a workaround for multi-tenant. Always enumerate the specific issuers you trust.

---

## Entra External ID (CIAM) vs. Workforce Tenant

The authority URL format differs. This affects `Authority`, `ValidIssuers`, and OIDC discovery:

| Tenant Type | Authority URL Format |
|---|---|
| Workforce (AAD) | `https://login.microsoftonline.com/{tenant-id}/v2.0` |
| External ID (CIAM) | `https://{your-tenant}.ciamlogin.com/{tenant-id}/v2.0` |
| Multi-tenant workforce | `https://login.microsoftonline.com/common/v2.0` |

CIAM tokens:
- Use the CIAM `iss` value — confirm with token inspector (`jwt.ms`)
- `scp` claim may contain just `notes.readwrite` (no `api://` prefix), so check both forms in your scope policy assertion
- The `tid` claim is the CIAM tenant ID, not a workforce tenant ID

---

## CurrentUserService: Account Linking and Race-Condition Handling

The `CurrentUserService` in Infrastructure does more than read claims — it provisions or resolves the internal `User` record on first login. Key design points:

**Claim priority order:**
```
oid  →  sub  →  ClaimTypes.NameIdentifier    (for externalId)
iss  →  tid  →  "UnknownIssuer"              (for provider)
email / preferred_username / upn             (for display)
```

**First-login race condition:** Two parallel requests for the same new user can both try to `INSERT` a `UserLogin` simultaneously. The implementation handles this by:

1. Attempting the insert
2. Catching `DbUpdateException` where `SqlException.Number == 2601 || 2627` (unique constraint violation)
3. Re-querying the now-existing row and returning that `UserId`

```csharp
catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
{
    // Another concurrent request created the user; just look it up
    var existing = await _dbContext.UserLogins
        .Include(ul => ul.User)
        .SingleAsync(ul => ul.Provider == provider && ul.ExternalId == externalId, ct);

    return existing.UserId;
}

private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    => ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
```

**Per-request caching:** `CurrentUserService` is scoped and caches `_cachedUserId` to avoid a DB hit on every claim access within the same request. This is safe because the service lifetime is per-request.

**Soft-deleted accounts:** If the found `User` has `IsDeleted = true`, an `InvalidOperationException` is thrown. The global exception handler converts this to a 500. Consider mapping it to 403 if you want a cleaner response for suspended accounts.
