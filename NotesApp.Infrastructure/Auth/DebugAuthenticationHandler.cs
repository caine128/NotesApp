using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace NotesApp.Infrastructure.Auth
{
    /// <summary>
    /// Development-only auth handler that authenticates a user if
    /// the request contains the header X-Debug-User.
    /// 
    /// Enabled only in Development environment.
    /// </summary>
    public sealed class DebugAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Debug";

        public DebugAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Look for a header like: X-Debug-User: sebastian
            if (!Request.Headers.TryGetValue("X-Debug-User", out var values) ||
                string.IsNullOrWhiteSpace(values.FirstOrDefault()))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var debugUserId = values.First();

            // Build a fake identity with some claims
            var claims = new List<Claim>
            {
                // Subject / unique identifier; could be anything stable
                new Claim(ClaimTypes.NameIdentifier, debugUserId),
                new Claim(ClaimTypes.Name, debugUserId),

                // Optional: give a fake "scp" claim so scope policy passes
                new Claim("scp", "api://d1047ffd-a054-4a9f-aeb0-198996f0c0c6/notes.readwrite")
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
