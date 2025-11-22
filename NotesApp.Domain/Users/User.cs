using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Users
{
    /// <summary>
    /// Our internal representation of a person using the app.
    /// All application data (Tasks, Notes, etc.) will reference this Id.
    /// </summary>
    public sealed class User : Entity<Guid>
    {
        /// <summary>
        /// Normalized email (lowercase, trimmed).
        /// Used for display / contact and optional login flows.
        /// </summary>
        public string Email { get; private set; } = string.Empty;

        /// <summary>
        /// Optional display name we show in the UI (e.g., "Sebastian").
        /// </summary>
        public string? DisplayName { get; private set; }

        /// <summary>
        /// Navigation: collection of external identities (Microsoft/Google/Apple).
        /// </summary>
        public IReadOnlyCollection<UserLogin> Logins => _logins.AsReadOnly();
        private readonly List<UserLogin> _logins = new();

        // EF Core parameterless constructor
        private User()
        {
        }

        private User(Guid id, string email, string? displayName, DateTime utcNow)
            : base(id, utcNow)
        {
            Email = email;
            DisplayName = displayName;
        }

        /// <summary>
        /// Factory method to create a new user with minimal invariants.
        /// </summary>
        public static DomainResult<User> Create(string? email, string? displayName, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                errors.Add(new DomainError("User.Email.Empty", "Email is required for a user."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<User>.Failure(errors);
            }

            var id = Guid.NewGuid();

            var user = new User(
                id,
                normalizedEmail,
                displayName?.Trim(),
                utcNow);

            return DomainResult<User>.Success(user);
        }

        /// <summary>
        /// Update profile fields. We keep validation minimal here;
        /// more complex rules can live in the Application layer.
        /// </summary>
        public DomainResult UpdateProfile(string? email, string? displayName, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                errors.Add(new DomainError("User.Email.Empty", "Email is required for a user."));
            }

            if (IsDeleted)
            {
                errors.Add(new DomainError("User.Deleted", "Cannot update a deleted user."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            Email = normalizedEmail;
            DisplayName = displayName?.Trim();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Soft-delete wrapper around base MarkDeleted. We keep the behavior
        /// explicit and consistent with our DomainResult pattern.
        /// </summary>
        public DomainResult Delete(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Success(); // idempotent
            }

            MarkDeleted(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Restore a previously deleted user.
        /// </summary>
        public DomainResult RestoreUser(DateTime utcNow)
        {
            if (!IsDeleted)
            {
                return DomainResult.Success(); // idempotent
            }

            Restore(utcNow);
            return DomainResult.Success();
        }

        internal void AddLogin(UserLogin login)
        {
            _logins.Add(login);
        }
    }
}
