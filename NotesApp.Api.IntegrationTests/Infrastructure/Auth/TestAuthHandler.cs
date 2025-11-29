using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Auth
{
    /// <summary>
    /// Test authentication handler used in integration tests.
    ///
    /// - Always authenticates the request.
    /// - Reads a user id from the "X-Test-UserId" header if present.
    /// - Otherwise, uses a fixed default user id.
    ///
    /// This lets each test run under its own "fake" user by just setting a header.
    /// </summary>
    internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestAuth";
        public const string UserIdHeaderName = "X-Test-UserId";
        public const string ScopeHeaderName = "X-Test-Scopes";

        // You can also use a static default Guid here if you prefer
        private static readonly Guid DefaultUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                               ILoggerFactory logger,
                               UrlEncoder encoder,
                               ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        /// <summary>
        /// Creates a ClaimsPrincipal for the current request, using a user id
        /// from the header if provided. This is what powers ICurrentUserService
        /// in the app under test.
        /// </summary>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Try to read a user id from the test header.
            var headerValue = Context.Request.Headers[UserIdHeaderName].FirstOrDefault();
            
            // Optional scopes header for tests that need to simulate API scopes
            var scopeHeader = Context.Request.Headers[ScopeHeaderName].FirstOrDefault();
            var scopeValue = string.IsNullOrWhiteSpace(scopeHeader)
                ? null
                : scopeHeader;

            // If no header or invalid GUID => treat as unauthenticated.
            if (string.IsNullOrWhiteSpace(headerValue) || !Guid.TryParse(headerValue, out var userId))
            {
                // This tells ASP.NET Core: "no user from this scheme".
                // [Authorize] will then result in 401.
                return Task.FromResult(AuthenticateResult.NoResult());
            }

           
            // Claims aligned with what your CurrentUserService expects
            var claims = new List<Claim>
            {
                 // Entra-style user id
                 new("oid", userId.ToString()),

                 // Standard OIDC subject and .NET NameIdentifier for compatibility
                 new("sub", userId.ToString()),
                 new(ClaimTypes.NameIdentifier, userId.ToString()),

                 new(ClaimTypes.Name, "Integration Test User"),

                 // Email (so User.Create has something meaningful)
                 new(ClaimTypes.Email, "integration.test.user@example.com"),
                 new("email", "integration.test.user@example.com"),

                 // Simulated issuer & tenant id for provider resolution
                 new("iss", "https://test.local"),
                 new("tid", "00000000-0000-0000-0000-000000000000"),
        };
            // ✅ Only add "scp" claim if we actually have a scope value
            if (!string.IsNullOrWhiteSpace(scopeValue))
            {
                claims.Add(new Claim("scp", scopeValue));
            }
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            var result = AuthenticateResult.Success(ticket);
            return Task.FromResult(result);
        }
    }
}
