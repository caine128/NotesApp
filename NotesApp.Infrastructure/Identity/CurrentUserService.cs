using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace NotesApp.Infrastructure.Identity
{
    /// <summary>
    /// Concrete implementation of ICurrentUserService.
    ///
    /// - Reads claims from HttpContext.User
    /// - Resolves or creates a User/UserLogin in the database
    /// - Caches the resolved UserId for the lifetime of the request
    ///
    /// This class lives in Infrastructure because it depends on:
    /// - HttpContext (web-specific)
    /// - EF Core DbContext (persistence)
    /// </summary>
    public sealed class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<CurrentUserService> _logger;

        // Per-request cache to avoid multiple DB hits for the same request.
        private Guid? _cachedUserId;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor,
                                  AppDbContext dbContext,
                                  ILogger<CurrentUserService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Returns the current authenticated user's internal Id (User.Id).
        /// Throws InvalidOperationException if there is no authenticated user.
        /// </summary>
        public async Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default)
        {
            if (_cachedUserId.HasValue)
            {
                return _cachedUserId.Value;
            }

            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext is null)
            {
                throw new InvalidOperationException(
                    "No HttpContext is available. ICurrentUserService can only be used in HTTP requests.");
            }

            var principal = httpContext.User;

            if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
            {
                throw new InvalidOperationException(
                    "No authenticated user found in the current HttpContext.");
            }

            // --- 1) Extract core claims from the principal ---

            // Prefer Entra's 'oid' for user identity when available.
            // Fall back to 'sub', then NameIdentifier for other providers.
            var externalId =
                principal.FindFirst("oid")?.Value ??
                principal.FindFirst("sub")?.Value ??
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(externalId))
            {
                throw new InvalidOperationException(
                    "Cannot resolve current user because none of 'oid', 'sub', or NameIdentifier claims are present.");
            }

            // Provider: prefer issuer URL ('iss'); if not present, fall back to tenant id ('tid') or a default label.
            var provider =
                principal.FindFirst("iss")?.Value ??
                principal.FindFirst("tid")?.Value ??
                "UnknownIssuer";

            // Email & display name are optional in the token; we do best-effort extraction.
            // For Entra ID, "preferred_username" is very often the user's email address.
            var email =
                principal.FindFirst(ClaimTypes.Email)?.Value ??
                principal.FindFirst("email")?.Value ??
                principal.FindFirst("preferred_username")?.Value ??
                principal.FindFirst("upn")?.Value;

            var displayName =
                principal.FindFirst(ClaimTypes.Name)?.Value ??
                principal.FindFirst("name")?.Value ??
                principal.FindFirst("preferred_username")?.Value;

            // Timestamp for domain audit fields
            var utcNow = DateTime.UtcNow;

            // --- 2) Resolve or create User/UserLogin in the database ---

            var userId = await GetOrCreateUserAsync(provider,
                                                    externalId,
                                                    email,
                                                    displayName,
                                                    utcNow,
                                                    cancellationToken);

            _cachedUserId = userId;
            return userId;
        }

        /// <summary>
        /// Core "account linking" logic:
        /// - (Provider, ExternalId) uniquely identifies an external account.
        /// - We look up UserLogins. If found, we reuse the existing UserId.
        /// - If not found, we create User + UserLogin and handle race conditions
        ///   via the unique index and a DbUpdateException retry.
        /// </summary>
        private async Task<Guid> GetOrCreateUserAsync(string provider,
                                                      string externalId,
                                                      string? email,
                                                      string? displayName,
                                                      DateTime utcNow,
                                                      CancellationToken cancellationToken)
        {
            // First, try to find an existing login
            var existingLogin = await _dbContext.UserLogins
                .Include(ul => ul.User)
                .Where(ul => ul.Provider == provider && ul.ExternalId == externalId)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingLogin is not null)
            {
                if (existingLogin.User.IsDeleted)
                {
                    _logger.LogWarning(
                        "UserLogin found for provider {Provider} and externalId {ExternalId} " +
                        "but the associated User (Id: {UserId}) is soft-deleted.",
                        provider, externalId, existingLogin.UserId);

                    // TODO : You could decide to restore or reject here.
                    // For now, we treat this as an error:
                    throw new InvalidOperationException(
                        "The associated user account is deleted. Access is not allowed.");
                }

                return existingLogin.UserId;
            }

            // No login found => first time we see this external account.
            _logger.LogInformation(
                "No existing UserLogin found for provider {Provider} and externalId {ExternalId}. " +
                "Attempting to create a new User + UserLogin.",
                provider, externalId);

            // 1) Create User domain object
            var userResult = User.Create(email, displayName, utcNow);

            if (userResult.IsFailure)
            {
                // This is a domain modeling choice: for now we fail fast if
                // the IdP didn't provide a valid email. You may later relax
                // this to allow email-less accounts.
                var summary = string.Join("; ", userResult.Errors.Select(e => e.Message));

                _logger.LogError(
                    "Failed to create User for provider {Provider}, externalId {ExternalId}. Errors: {Errors}",
                    provider, externalId, summary);

                throw new InvalidOperationException(
                    $"Cannot create user from external identity: {summary}");
            }

            var user = userResult.Value;

            // 2) Create UserLogin domain object
            var loginResult = UserLogin.Create(user, provider, externalId, null, utcNow);

            if (loginResult.IsFailure)
            {
                var summary = string.Join("; ", loginResult.Errors.Select(e => e.Message));

                _logger.LogError(
                    "Failed to create UserLogin for provider {Provider}, externalId {ExternalId}. Errors: {Errors}",
                    provider, externalId, summary);

                throw new InvalidOperationException(
                    $"Cannot create user login from external identity: {summary}");
            }

            var login = loginResult.Value;

            _dbContext.Users.Add(user);
            _dbContext.UserLogins.Add(login);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Successfully created User (Id: {UserId}) and UserLogin for provider {Provider}, externalId {ExternalId}.",
                    user.Id, provider, externalId);

                return user.Id;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Another concurrent request created the login/user just before us.
                _logger.LogWarning(
                    ex,
                    "Unique constraint violation when creating UserLogin for provider {Provider}, externalId {ExternalId}. " +
                    "Assuming another request created the user. Retrying lookup.",
                    provider, externalId);

                var existingAfterRace = await _dbContext.UserLogins
                    .Include(ul => ul.User)
                    .SingleAsync(
                        ul => ul.Provider == provider && ul.ExternalId == externalId,
                        cancellationToken);

                if (existingAfterRace.User.IsDeleted)
                {
                    throw new InvalidOperationException(
                        "The associated user account is deleted after race-resolution. Access is not allowed.");
                }

                return existingAfterRace.UserId;
            }
        }

        /// <summary>
        /// Detects whether a DbUpdateException was caused by a SQL Server
        /// unique constraint / unique index violation.
        ///
        /// Error codes:
        /// - 2601: Cannot insert duplicate key row in object with unique index.
        /// - 2627: Violation of PRIMARY KEY constraint or UNIQUE KEY constraint.
        ///
        /// This pattern is widely used when handling EF Core unique indexes
        /// on SQL Server.
        /// </summary>
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlEx)
            {
                return sqlEx.Number == 2601 || sqlEx.Number == 2627;
            }

            return false;
        }
    }
}
