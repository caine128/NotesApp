using FluentAssertions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Devices
{
    /// <summary>
    /// Tests for UserDeviceRepository using the SQL Server test AppDbContext.
    /// Mirrors the style of NoteRepositoryTests.
    /// </summary>
    public sealed class UserDeviceRepositoryTests
    {
        private static User CreateUser(string email, string? displayName, DateTime utcNow)
        {
            var result = User.Create(email, displayName, utcNow);
            result.IsSuccess.Should().BeTrue("test setup must use valid user data");
            return result.Value!;
        }

        [Fact]
        public async Task GetByTokenAsync_returns_device_even_if_soft_deleted()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            IUserDeviceRepository repository = new UserDeviceRepository(context);

            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var user = CreateUser("user1@example.com", "User 1", utcNow);
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var device = UserDevice.Create(
                user.Id,
                "token-123",
                DevicePlatform.Android,
                "My phone",
                utcNow).Value!;

            // Soft-delete / deactivate the device
            device.Deactivate(utcNow.AddMinutes(1));

            await context.UserDevices.AddAsync(device);
            await context.SaveChangesAsync();

            // Act
            var found = await repository.GetByTokenAsync("  token-123  ", CancellationToken.None);

            // Assert
            found.Should().NotBeNull();
            found!.Id.Should().Be(device.Id);
            found.DeviceToken.Should().Be(device.DeviceToken);
            found.IsActive.Should().BeFalse();
            found.IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task GetActiveDevicesForUserAsync_returns_only_active_and_not_deleted_for_user()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            IUserDeviceRepository repository = new UserDeviceRepository(context);

            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var user = CreateUser("user1@example.com", "User 1", utcNow);
            var otherUser = CreateUser("user2@example.com", "User 2", utcNow);

            await context.Users.AddRangeAsync(user, otherUser);
            await context.SaveChangesAsync();

            // Devices for current user
            var active = UserDevice.Create(
                user.Id,
                "active-1",
                DevicePlatform.Android,
                "Active 1",
                utcNow).Value!;

            var inactive = UserDevice.Create(
                user.Id,
                "inactive",
                DevicePlatform.Android,
                "Inactive",
                utcNow).Value!;

            inactive.Deactivate(utcNow.AddMinutes(1));

            // Device for another user
            var otherUsersDevice = UserDevice.Create(
                otherUser.Id,
                "other-1",
                DevicePlatform.Android,
                "Other",
                utcNow).Value!;

            await context.UserDevices.AddRangeAsync(active, inactive, otherUsersDevice);
            await context.SaveChangesAsync();

            // Act
            var result = await repository.GetActiveDevicesForUserAsync(user.Id, CancellationToken.None);

            // Assert
            var list = result.ToList();
            list.Should().HaveCount(1);
            list[0].Id.Should().Be(active.Id);
            list[0].IsActive.Should().BeTrue();
            list[0].IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task GetActiveDevicesForUserExceptAsync_excludes_given_deviceId()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            IUserDeviceRepository repository = new UserDeviceRepository(context);

            var utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

            var user = CreateUser("user1@example.com", "User 1", utcNow);

            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var keepDevice = UserDevice.Create(
                user.Id,
                "keep-token",
                DevicePlatform.Android,
                "Keep",
                utcNow).Value!;

            var excludedDevice = UserDevice.Create(
                user.Id,
                "excluded",
                DevicePlatform.Android,
                "Excluded",
                utcNow).Value!;

            await context.UserDevices.AddRangeAsync(keepDevice, excludedDevice);
            await context.SaveChangesAsync();

            // Act
            var result = await repository.GetActiveDevicesForUserExceptAsync(
                user.Id,
                excludedDevice.Id,
                CancellationToken.None);

            // Assert
            var list = result.ToList();
            list.Should().HaveCount(1);
            list[0].Id.Should().Be(keepDevice.Id);
            list[0].IsActive.Should().BeTrue();
            list[0].IsDeleted.Should().BeFalse();
        }
    }
}
