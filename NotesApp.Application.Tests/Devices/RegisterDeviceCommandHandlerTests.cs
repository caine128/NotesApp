using FluentAssertions;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Devices.Commands.RegisterDevice;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Devices
{
    public sealed class RegisterDeviceCommandHandlerTests
    {
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();

        private readonly Guid _currentUserId = Guid.NewGuid();
        private readonly DateTime _utcNow = new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

        private RegisterDeviceCommandHandler CreateSut()
        {
            _currentUserServiceMock
                .Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_currentUserId);

            _clockMock
                .Setup(x => x.UtcNow)
                .Returns(_utcNow);

            return new RegisterDeviceCommandHandler(
                _deviceRepositoryMock.Object,
                _currentUserServiceMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object);
        }

        [Fact]
        public async Task Handle_with_new_token_creates_device_and_returns_dto()
        {
            // Arrange
            var sut = CreateSut();

            _deviceRepositoryMock
                .Setup(x => x.GetByTokenAsync("token-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserDevice?)null);

            var command = new RegisterDeviceCommand
            {
                DeviceToken = "  token-123  ",
                Platform = DevicePlatform.Android,
                DeviceName = "  My Phone  "
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();

            var dto = result.Value;
            dto.DeviceToken.Should().Be("token-123");
            dto.Platform.Should().Be(DevicePlatform.Android);
            dto.DeviceName.Should().Be("My Phone");
            dto.IsActive.Should().BeTrue();
            dto.LastSeenAtUtc.Should().Be(_utcNow);

            _deviceRepositoryMock.Verify(
                x => x.AddAsync(It.IsAny<UserDevice>(), It.IsAny<CancellationToken>()),
                Times.Once);

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_with_existing_token_for_same_user_reactivates_and_updates_name()
        {
            // Arrange
            var existing = UserDevice.Create(
                    _currentUserId,
                    "token-123",
                    DevicePlatform.Android,
                    "Old Name",
                    _utcNow.AddMinutes(-10))
                .Value!;

            var sut = CreateSut();

            _deviceRepositoryMock
                .Setup(x => x.GetByTokenAsync("token-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            var command = new RegisterDeviceCommand
            {
                DeviceToken = "token-123",
                Platform = DevicePlatform.Android,
                DeviceName = "New Name"
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Id.Should().Be(existing.Id);
            dto.DeviceName.Should().Be("New Name");
            dto.IsActive.Should().BeTrue();
            dto.LastSeenAtUtc.Should().Be(_utcNow);

            existing.UserId.Should().Be(_currentUserId);
            existing.DeviceName.Should().Be("New Name");
            existing.IsActive.Should().BeTrue();
            existing.LastSeenAtUtc.Should().Be(_utcNow);

            _deviceRepositoryMock.Verify(
                x => x.Update(existing),
                Times.Once);

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_with_existing_token_for_other_user_reassigns_device()
        {
            // Arrange
            var otherUserId = Guid.NewGuid();

            var existing = UserDevice.Create(
                    otherUserId,
                    "token-123",
                    DevicePlatform.Android,
                    "Other User Device",
                    _utcNow.AddMinutes(-10))
                .Value!;

            var sut = CreateSut();

            _deviceRepositoryMock
                .Setup(x => x.GetByTokenAsync("token-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            var command = new RegisterDeviceCommand
            {
                DeviceToken = "token-123",
                Platform = DevicePlatform.Android,
                DeviceName = "My Reclaimed Device"
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Id.Should().Be(existing.Id);
            dto.DeviceToken.Should().Be("token-123");
            dto.DeviceName.Should().Be("My Reclaimed Device");
            dto.IsActive.Should().BeTrue();

            existing.UserId.Should().Be(_currentUserId); // reassigned
            existing.DeviceName.Should().Be("My Reclaimed Device");
            existing.IsActive.Should().BeTrue();
            existing.LastSeenAtUtc.Should().Be(_utcNow);

            _deviceRepositoryMock.Verify(
                x => x.Update(existing),
                Times.Once);

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_when_domain_create_fails_returns_failed_result()
        {
            // Arrange
            var sut = CreateSut();

            // This bypasses validator in the unit test, so Create() will fail.
            var command = new RegisterDeviceCommand
            {
                DeviceToken = "   ", // invalid, becomes empty
                Platform = DevicePlatform.Android,
                DeviceName = null
            };

            // Act
            var result = await sut.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();

            _deviceRepositoryMock.Verify(
                x => x.AddAsync(It.IsAny<UserDevice>(), It.IsAny<CancellationToken>()),
                Times.Never);

            _unitOfWorkMock.Verify(
                x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
