using FluentAssertions;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Commands.UnregisterDevice;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Devices
{
    public sealed class UnregisterDeviceCommandHandlerTests
    {
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();

        private readonly Guid _currentUserId = Guid.NewGuid();
        private readonly DateTime _utcNow = new DateTime(2025, 2, 1, 13, 0, 0, DateTimeKind.Utc);

        private UnregisterDeviceCommandHandler CreateSut()
        {
            _currentUserServiceMock
                .Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_currentUserId);

            _clockMock
                .Setup(x => x.UtcNow)
                .Returns(_utcNow);

            return new UnregisterDeviceCommandHandler(
                _deviceRepositoryMock.Object,             
                _currentUserServiceMock.Object,
                 _unitOfWorkMock.Object,
                _clockMock.Object);
        }

        [Fact]
        public async Task Handle_when_device_not_found_returns_not_found_error()
        {
            // Arrange
            var sut = CreateSut();
            var deviceId = Guid.NewGuid();

            _deviceRepositoryMock
                .Setup(x => x.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserDevice?)null);

            var command = new UnregisterDeviceCommand
            {
                DeviceId = deviceId
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Message.Should().Contain("Device.NotFound");

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_when_device_belongs_to_other_user_returns_not_found_error()
        {
            // Arrange
            var sut = CreateSut();
            var deviceId = Guid.NewGuid();

            var otherUserDevice = UserDevice.Create(
                    Guid.NewGuid(), // other user
                    "token-123",
                    DevicePlatform.Android,
                    "Other User Device",
                    _utcNow.AddMinutes(-30))
                .Value!;

            // Force the Id to the requested id to simulate lookup
            typeof(UserDevice)
                .GetProperty(nameof(UserDevice.Id))!
                .SetValue(otherUserDevice, deviceId);

            _deviceRepositoryMock
                .Setup(x => x.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(otherUserDevice);

            var command = new UnregisterDeviceCommand
            {
                DeviceId = deviceId
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Message.Should().Contain("Device.NotFound");

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_when_device_belongs_to_current_user_deactivates_and_saves()
        {
            // Arrange
            var sut = CreateSut();
            var deviceId = Guid.NewGuid();

            var device = UserDevice.Create(
                    _currentUserId,
                    "token-123",
                    DevicePlatform.Android,
                    "My Device",
                    _utcNow.AddMinutes(-30))
                .Value!;

            typeof(UserDevice)
                .GetProperty(nameof(UserDevice.Id))!
                .SetValue(device, deviceId);

            _deviceRepositoryMock
                .Setup(x => x.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);

            var command = new UnregisterDeviceCommand
            {
                DeviceId = deviceId
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            device.IsActive.Should().BeFalse();
            device.IsDeleted.Should().BeTrue();
            device.UpdatedAtUtc.Should().Be(_utcNow);

            _deviceRepositoryMock.Verify(
                x => x.Update(device),
                Times.Once);

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
