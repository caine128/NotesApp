using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Users
{
    /// <summary>
    /// Represents a physical device (phone / tablet) that belongs to a user
    /// and can receive push notifications or participate in sync.
    /// </summary>
    public sealed class UserDevice : Entity<Guid>
    {
        // ENTITY CONSTANTS 
        public const int MaxDeviceTokenLength = 512;
        public const int MaxPlatformLength = 20;
        public const int MaxDeviceNameLength = 256;

        /// <summary>
        /// Owner of this device. All sync / push for this device is scoped to this user.
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// The push/token identifier from FCM/APNs (or similar).
        /// Normalized and trimmed.
        /// </summary>
        public string DeviceToken { get; private set; } = string.Empty;

        /// <summary>
        /// Platform of the device (Android / iOS / etc.).
        /// </summary>
        public DevicePlatform Platform { get; private set; }

        /// <summary>
        /// Optional user-friendly name ("Sebastian's iPhone", "Tablet", etc.).
        /// </summary>
        public string? DeviceName { get; private set; }

        /// <summary>
        /// Last time this device was seen (i.e., registered or used).
        /// </summary>
        public DateTime LastSeenAtUtc { get; private set; }

        /// <summary>
        /// Whether this device is currently active for push/sync.
        /// Soft delete is handled via the base IsDeleted flag.
        /// </summary>
        public bool IsActive { get; private set; }

        private UserDevice()
        {
            // EF Core
        }

        /// <summary>
        /// Factory method to create a new UserDevice.
        /// Handles validation and normalization.
        /// </summary>
        public static DomainResult<UserDevice> Create(Guid userId,
                                                      string deviceToken,
                                                      DevicePlatform platform,
                                                      string? deviceName,
                                                      DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedToken = deviceToken?.Trim() ?? string.Empty;
            var normalizedName = deviceName?.Trim();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("UserDevice.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                errors.Add(new DomainError("UserDevice.Token.Empty", "Device token must not be empty."));
            }

            if (platform == DevicePlatform.Unknown)
            {
                errors.Add(new DomainError("UserDevice.Platform.Unknown", "Device platform must be specified."));
            }

            if (normalizedName is not null && normalizedName.Length > MaxDeviceNameLength)
            {
                errors.Add(new DomainError("UserDevice.Name.TooLong",
                    $"Device name must be at most {MaxDeviceNameLength} characters long."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<UserDevice>.Failure(errors.ToArray());
            }

            var device = new UserDevice
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DeviceToken = normalizedToken,
                Platform = platform,
                DeviceName = normalizedName,
                LastSeenAtUtc = utcNow,
                IsActive = true,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
                IsDeleted = false
            };

            return DomainResult<UserDevice>.Success(device);
        }

        /// <summary>
        /// Updates the token (e.g., FCM token refresh) and touches timestamps.
        /// </summary>
        public DomainResult UpdateToken(string newToken, DateTime utcNow)
        {
            var normalized = newToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return DomainResult.Failure(
                    new DomainError("UserDevice.Token.Empty", "Device token must not be empty."));
            }

            DeviceToken = normalized;
            LastSeenAtUtc = utcNow;
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Updates the device name (optional).
        /// </summary>
        public DomainResult UpdateName(string? newName, DateTime utcNow)
        {
            // Normalize
            var normalized = string.IsNullOrWhiteSpace(newName)
                        ? null
                        : newName.Trim();

            if (normalized is not null && normalized.Length > MaxDeviceNameLength)
            {
                return DomainResult.Failure(
                    new DomainError("UserDevice.Name.TooLong",
                        $"Device name must be at most {MaxDeviceNameLength} characters long."));
            }

            DeviceName = normalized;
            LastSeenAtUtc = utcNow;
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Marks this device as seen/used now.
        /// </summary>
        public void TouchLastSeen(DateTime utcNow)
        {
            LastSeenAtUtc = utcNow;
            Touch(utcNow);
        }

        /// <summary>
        /// Deactivates this device (no further pushes/sync).
        /// </summary>
        public DomainResult Deactivate(DateTime utcNow)
        {
            if (!IsActive && IsDeleted)
            {
                // Already completely inactive/deleted; idempotent success.
                return DomainResult.Success();
            }

            IsActive = false;
            MarkDeleted(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Reactivates this device for the current user.
        /// </summary>
        public DomainResult Reactivate(DateTime utcNow)
        {
            if (IsActive && !IsDeleted)
            {
                // Already active; idempotent.
                return DomainResult.Success();
            }

            IsActive = true;
            Restore(utcNow);
            LastSeenAtUtc = utcNow;
            return DomainResult.Success();
        }

        /// <summary>
        /// Reassigns this device to a different user (token migration).
        /// </summary>
        public DomainResult ReassignToUser(Guid newUserId, DateTime utcNow)
        {
            if (newUserId == Guid.Empty)
            {
                return DomainResult.Failure(
                    new DomainError("UserDevice.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            UserId = newUserId;
            IsActive = true;
            IsDeleted = false;
            LastSeenAtUtc = utcNow;
            Touch(utcNow);
            return DomainResult.Success();
        }
    }

    /// <summary>
    /// Platform for a user device. Extend as needed.
    /// </summary>
    public enum DevicePlatform
    {
        Unknown = 0,
        Android = 1,
        IOS = 2
        // Later: Web, Windows, etc.
    }
}
