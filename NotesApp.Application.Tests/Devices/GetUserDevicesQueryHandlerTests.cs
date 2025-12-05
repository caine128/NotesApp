using FluentAssertions;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Queries.GetUserDevices;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Devices
{
    public sealed class GetUserDevicesQueryHandlerTests
    {
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

        private readonly Guid _currentUserId = Guid.NewGuid();

        private GetUserDevicesQueryHandler CreateSut()
        {
            _currentUserServiceMock
                .Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_currentUserId);

            return new GetUserDevicesQueryHandler(
                _deviceRepositoryMock.Object,
                _currentUserServiceMock.Object);
        }

        [Fact]
        public async Task Handle_returns_active_devices_for_current_user_mapped_to_dtos()
        {
            // Arrange
            var utcNow = new DateTime(2025, 2, 1, 14, 0, 0, DateTimeKind.Utc);

            var device1 = UserDevice.Create(
                    _currentUserId,
                    "token-1",
                    DevicePlatform.Android,
                    "Phone",
                    utcNow.AddMinutes(-10))
                .Value!;

            var device2 = UserDevice.Create(
                    _currentUserId,
                    "token-2",
                    DevicePlatform.IOS,
                    "Tablet",
                    utcNow.AddMinutes(-5))
                .Value!;

            var devices = new List<UserDevice> { device1, device2 };

            _deviceRepositoryMock
                .Setup(x => x.GetActiveDevicesForUserAsync(_currentUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(devices);

            var sut = CreateSut();
            var query = new GetUserDevicesQuery();

            // Act
            var result = await sut.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().HaveCount(2);

            var dto1 = result.Value[0];
            var dto2 = result.Value[1];

            dto1.Id.Should().Be(device1.Id);
            dto1.DeviceToken.Should().Be(device1.DeviceToken);
            dto1.Platform.Should().Be(device1.Platform);
            dto1.DeviceName.Should().Be(device1.DeviceName);
            dto1.LastSeenAtUtc.Should().Be(device1.LastSeenAtUtc);
            dto1.IsActive.Should().Be(device1.IsActive);

            dto2.Id.Should().Be(device2.Id);
            dto2.DeviceToken.Should().Be(device2.DeviceToken);
            dto2.Platform.Should().Be(device2.Platform);
            dto2.DeviceName.Should().Be(device2.DeviceName);
            dto2.LastSeenAtUtc.Should().Be(device2.LastSeenAtUtc);
            dto2.IsActive.Should().Be(device2.IsActive);

            _deviceRepositoryMock.Verify(
                x => x.GetActiveDevicesForUserAsync(_currentUserId, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_when_no_devices_returns_empty_list()
        {
            // Arrange
            _deviceRepositoryMock
                .Setup(x => x.GetActiveDevicesForUserAsync(_currentUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserDevice>());

            var sut = CreateSut();
            var query = new GetUserDevicesQuery();

            // Act
            var result = await sut.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeEmpty();

            _deviceRepositoryMock.Verify(
                x => x.GetActiveDevicesForUserAsync(_currentUserId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
