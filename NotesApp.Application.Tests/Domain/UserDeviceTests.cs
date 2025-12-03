using FluentAssertions;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Domain
{
    public sealed class UserDeviceTests
    {
        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_properties()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            var result = UserDevice.Create(
                userId: userId,
                deviceToken: "  token-123  ",
                platform: DevicePlatform.Android,
                deviceName: "  Sebastian's Phone  ",
                utcNow: utcNow);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var device = result.Value!;
            device.Id.Should().NotBe(Guid.Empty);
            device.UserId.Should().Be(userId);
            device.DeviceToken.Should().Be("token-123");
            device.Platform.Should().Be(DevicePlatform.Android);
            device.DeviceName.Should().Be("Sebastian's Phone");
            device.LastSeenAtUtc.Should().Be(utcNow);
            device.IsActive.Should().BeTrue();
            device.CreatedAtUtc.Should().Be(utcNow);
            device.UpdatedAtUtc.Should().Be(utcNow);
            device.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void Create_with_empty_userId_returns_failure()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = UserDevice.Create(
                userId: Guid.Empty,
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: null,
                utcNow: utcNow);

            result.IsFailure.Should().BeTrue();
        }

        [Fact]
        public void Create_with_empty_token_returns_failure()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var result = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "   ",
                platform: DevicePlatform.Android,
                deviceName: null,
                utcNow: utcNow);

            result.IsFailure.Should().BeTrue();
        }

        [Fact]
        public void Create_with_too_long_name_returns_failure()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var veryLongName = new string('x', 300);

            var result = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: veryLongName,
                utcNow: utcNow);

            result.IsFailure.Should().BeTrue();
        }

        [Fact]
        public void UpdateToken_with_valid_token_updates_and_touches()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var later = utcNow.AddMinutes(5);

            var device = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "old-token",
                platform: DevicePlatform.Android,
                deviceName: null,
                utcNow: utcNow).Value!;

            var result = device.UpdateToken("  new-token  ", later);

            result.IsSuccess.Should().BeTrue();
            device.DeviceToken.Should().Be("new-token");
            device.LastSeenAtUtc.Should().Be(later);
            device.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void UpdateToken_with_empty_token_returns_failure_and_does_not_change_state()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var later = utcNow.AddMinutes(5);

            var device = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: null,
                utcNow: utcNow).Value!;

            var result = device.UpdateToken("   ", later);

            result.IsFailure.Should().BeTrue();
            device.DeviceToken.Should().Be("token");
            device.LastSeenAtUtc.Should().Be(utcNow);
            device.UpdatedAtUtc.Should().Be(utcNow);
        }

        [Fact]
        public void UpdateName_with_null_or_whitespace_clears_name_and_touches()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var later = utcNow.AddMinutes(5);

            var device = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: "Initial",
                utcNow: utcNow).Value!;

            var result = device.UpdateName("   ", later);

            result.IsSuccess.Should().BeTrue();
            device.DeviceName.Should().BeNull();
            device.LastSeenAtUtc.Should().Be(later);
            device.UpdatedAtUtc.Should().Be(later);
        }

        [Fact]
        public void UpdateName_with_too_long_name_returns_failure_and_does_not_change_state()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var later = utcNow.AddMinutes(5);
            var veryLongName = new string('x', 300);

            var device = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: "Initial",
                utcNow: utcNow).Value!;

            var result = device.UpdateName(veryLongName, later);

            result.IsFailure.Should().BeTrue();
            device.DeviceName.Should().Be("Initial");
            device.LastSeenAtUtc.Should().Be(utcNow);
            device.UpdatedAtUtc.Should().Be(utcNow);
        }

        [Fact]
        public void Deactivate_and_reactivate_are_idempotent_and_update_flags()
        {
            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var later = utcNow.AddMinutes(5);

            var device = UserDevice.Create(
                userId: Guid.NewGuid(),
                deviceToken: "token",
                platform: DevicePlatform.Android,
                deviceName: null,
                utcNow: utcNow).Value!;

            var deactivateResult1 = device.Deactivate(later);
            deactivateResult1.IsSuccess.Should().BeTrue();
            device.IsActive.Should().BeFalse();
            device.IsDeleted.Should().BeTrue();

            var deactivateResult2 = device.Deactivate(later.AddMinutes(1));
            deactivateResult2.IsSuccess.Should().BeTrue();

            var reactivateResult1 = device.Reactivate(later.AddMinutes(2));
            reactivateResult1.IsSuccess.Should().BeTrue();
            device.IsActive.Should().BeTrue();
            device.IsDeleted.Should().BeFalse();

            var reactivateResult2 = device.Reactivate(later.AddMinutes(3));
            reactivateResult2.IsSuccess.Should().BeTrue();
        }
    }
}
