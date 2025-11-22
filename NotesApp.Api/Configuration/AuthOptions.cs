using System.ComponentModel.DataAnnotations;

namespace NotesApp.Api.Configuration
{
    /// <summary>
    /// Strongly-typed configuration for JWT bearer authentication.
    /// These values come from configuration (user secrets, env vars, etc.).
    /// </summary>
    public sealed class AuthOptions
    {
        /// <summary>
        /// Authority (issuer URL) of the identity provider.
        /// Example (workforce tenant):
        ///   https://login.microsoftonline.com/{tenant-id}/v2.0
        /// Example (Entra External ID CIAM tenant):
        ///   https://{your-tenant}.ciamlogin.com/{tenant-id}/v2.0
        /// </summary>
        [Required]
        [Url]
        public string Authority { get; init; } = default!;

        /// <summary>
        /// Audience (the API's "Application ID URI" or client ID) that access tokens must be issued for.
        /// Example:
        ///   api://{your-api-client-id}
        ///   or just the client ID GUID, depending on your setup.
        /// </summary>
        [Required]
        public string Audience { get; init; } = default!;

        /// <summary>
        /// Optional explicit list of valid issuers if you want to support multiple.
        /// If null/empty, Authority is used as the single issuer.
        /// </summary>
        public string[]? ValidIssuers { get; init; }

        /// <summary>
        /// How much clock skew to tolerate when validating token lifetimes.
        /// Default is 1 minute (we override the framework's default 5 minutes
        /// as recommended when servers are NTP-synced).
        /// </summary>
        [Range(0, 10)]
        public int ClockSkewMinutes { get; init; } = 1;

        /// <summary>
        /// Claim that will be used as "Name" if you need it in the app.
        /// We mostly read "sub" and "iss" directly, but this keeps things explicit.
        /// </summary>
        public string NameClaimType { get; init; } = "name";

        /// <summary>
        /// Claim used for roles if you ever use policy-based authorization.
        /// </summary>
        public string RoleClaimType { get; init; } = "roles";
    }
}
