using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Notifications;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notifications
{
    public sealed class LoggingPushNotificationServiceTests
    {
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<ILogger<LoggingPushNotificationService>> _loggerMock = new();

        private LoggingPushNotificationService CreateSut()
            => new LoggingPushNotificationService(_deviceRepositoryMock.Object, _loggerMock.Object);

        [Fact]
        public async Task SendSyncNeededAsync_without_originDeviceId_queries_all_active_devices()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var ct = CancellationToken.None;

            var devices = new List<UserDevice>
            {
                UserDevice.Create(
                    userId,
                    deviceToken: "token-1",
                    platform: DevicePlatform.Android,
                    deviceName: "Pixel 9",
                    utcNow: DateTime.UtcNow).Value!,
                UserDevice.Create(
                    userId,
                    deviceToken: "token-2",
                    platform: DevicePlatform.IOS,
                    deviceName: "iPhone 17",
                    utcNow: DateTime.UtcNow).Value!,
            };

            _deviceRepositoryMock
                .Setup(r => r.GetActiveDevicesForUserAsync(userId, ct))
                .ReturnsAsync(devices);

            var sut = CreateSut();

            // Act
            var result = await sut.SendSyncNeededAsync(userId, originDeviceId: null, ct);

            // Assert
            result.IsSuccess.Should().BeTrue();

            _deviceRepositoryMock.Verify(
                r => r.GetActiveDevicesForUserAsync(userId, ct),
                Times.Once);

            _deviceRepositoryMock.Verify(
                r => r.GetActiveDevicesForUserExceptAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SendSyncNeededAsync_with_originDeviceId_queries_all_other_devices()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();
            var ct = CancellationToken.None;

            var devices = new List<UserDevice>
            {
                UserDevice.Create(
                    userId,
                    deviceToken: "token-3",
                    platform: DevicePlatform.Android,
                    deviceName: "Galaxy Ultra",
                    utcNow: DateTime.UtcNow).Value!,
            };

            _deviceRepositoryMock
                .Setup(r => r.GetActiveDevicesForUserExceptAsync(userId, originDeviceId, ct))
                .ReturnsAsync(devices);

            var sut = CreateSut();

            // Act
            var result = await sut.SendSyncNeededAsync(userId, originDeviceId, ct);

            // Assert
            result.IsSuccess.Should().BeTrue();

            _deviceRepositoryMock.Verify(
                r => r.GetActiveDevicesForUserExceptAsync(userId, originDeviceId, ct),
                Times.Once);

            _deviceRepositoryMock.Verify(
                r => r.GetActiveDevicesForUserAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task SendSyncNeededAsync_with_no_target_devices_still_succeeds()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var ct = CancellationToken.None;

            _deviceRepositoryMock
                .Setup(r => r.GetActiveDevicesForUserAsync(userId, ct))
                .ReturnsAsync(new List<UserDevice>());

            var sut = CreateSut();

            // Act
            var result = await sut.SendSyncNeededAsync(userId, originDeviceId: null, ct);

            // Assert
            result.IsSuccess.Should().BeTrue();

            _deviceRepositoryMock.Verify(
                r => r.GetActiveDevicesForUserAsync(userId, ct),
                Times.Once);
        }
    }
}
