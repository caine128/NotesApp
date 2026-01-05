using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Users
{
    /// <summary>
    /// Links an external identity provider (Microsoft/Google/Apple)
    /// to our internal User.
    /// </summary>
    public sealed class UserLogin : Entity<Guid>
    {
        // ENTITY CONSTANTS 
        public const int MaxProviderLength = 100;   
        public const int MaxExternalIdLength = 200;
        public const int MaxProviderDisplayNameLength = 200;

        /// <summary>
        /// FK to Users table. All logins belong to exactly one User.
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// Provider name, e.g. "Microsoft", "Google", "Apple".
        /// </summary>
        public string Provider { get; private set; } = string.Empty;

        /// <summary>
        /// The stable external ID from the provider (e.g. oid / sub).
        /// </summary>
        public string ExternalId { get; private set; } = string.Empty;

        /// <summary>
        /// Optional friendly name for the provider (e.g. "Microsoft Entra ID").
        /// </summary>
        public string? ProviderDisplayName { get; private set; }

        /// <summary>
        /// Navigation back to User.
        /// </summary>
        public User User { get; private set; } = null!;

        // EF Core
        private UserLogin()
        {
        }

        private UserLogin(Guid id,
                          Guid userId,
                          string provider,
                          string externalId,
                          string? providerDisplayName,
                          DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            Provider = provider;
            ExternalId = externalId;
            ProviderDisplayName = providerDisplayName;
        }

        /// <summary>
        /// Factory for creating a new login record. We enforce basic invariants.
        /// </summary>
        public static DomainResult<UserLogin> Create(User user,
                                                     string? provider,
                                                     string? externalId,
                                                     string? providerDisplayName,
                                                     DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (user is null)
            {
                errors.Add(new DomainError("UserLogin.User.Null", "User cannot be null when creating a login."));
            }

            var normalizedProvider = (provider ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedProvider))
            {
                errors.Add(new DomainError("UserLogin.Provider.Empty", "Provider is required."));
            }

            var normalizedExternalId = (externalId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedExternalId))
            {
                errors.Add(new DomainError("UserLogin.ExternalId.Empty", "ExternalId is required."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<UserLogin>.Failure(errors);
            }

            var id = Guid.NewGuid();
            var login = new UserLogin(id,
                                      user.Id,
                                      normalizedProvider,
                                      normalizedExternalId,
                                      providerDisplayName?.Trim(),
                                      utcNow);

            user.AddLogin(login);

            return DomainResult<UserLogin>.Success(login);
        }
    }
}
