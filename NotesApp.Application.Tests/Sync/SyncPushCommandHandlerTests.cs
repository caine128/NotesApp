using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Sync
{
    public sealed class SyncPushCommandHandlerTests
    {
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<INoteRepository> _noteRepositoryMock = new();
        private readonly Mock<IBlockRepository> _blockRepositoryMock = new();
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<SyncPushCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _deviceId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private SyncPushCommandHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            _clockMock
                .Setup(c => c.UtcNow)
                .Returns(_now);

            _unitOfWorkMock
                .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var device = UserDevice.Create(
                _userId,
                "test-token",
                DevicePlatform.Android,
                "Test device",
                _now).Value!;

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(_deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);

            return new SyncPushCommandHandler(
                _currentUserServiceMock.Object,
                _taskRepositoryMock.Object,
                _noteRepositoryMock.Object,
                _blockRepositoryMock.Object,
                _deviceRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        #region Device Validation Tests

        [Fact]
        public async Task Handle_WithNonexistentDevice_ReturnsDeviceNotFound()
        {
            // Arrange
            var handler = CreateHandler();

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(_deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserDevice?)null);

            var command = CreateEmptyCommand();

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        [Fact]
        public async Task Handle_WithDeviceBelongingToDifferentUser_ReturnsDeviceNotFound()
        {
            // Arrange
            var handler = CreateHandler();

            var otherUserId = Guid.NewGuid();
            var foreignDevice = UserDevice.Create(
                otherUserId,
                "foreign-token",
                DevicePlatform.IOS,
                "Foreign device",
                _now).Value!;

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(_deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignDevice);

            var command = CreateEmptyCommand();

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        [Fact]
        public async Task Handle_WithInactiveDevice_ReturnsDeviceNotFound()
        {
            // Arrange
            var handler = CreateHandler();

            var device = UserDevice.Create(
                _userId,
                "token-inactive",
                DevicePlatform.Android,
                "Inactive device",
                _now).Value!;

            device.Deactivate(_now);

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(_deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);

            var command = CreateEmptyCommand();

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        #endregion

        #region Task Create Tests

        [Fact]
        public async Task Handle_TaskCreate_Success_ReturnsCreatedWithServerIdAndVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                    new TaskCreatedPushItemDto
                    {
                        ClientId = clientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "New Task",
                        Description = "Description"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            taskResult.ClientId.Should().Be(clientId);
            taskResult.ServerId.Should().NotBeEmpty();
            taskResult.Version.Should().BeGreaterThanOrEqualTo(1);
            taskResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            taskResult.Conflict.Should().BeNull();

            _taskRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TaskCreate_WithReminder_SetsReminderOnTask()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();
            var reminderTime = _now.AddHours(2);

            TaskItem? capturedTask = null;
            _taskRepositoryMock
                .Setup(r => r.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()))
                .Callback<TaskItem, CancellationToken>((t, _) => capturedTask = t)
                .Returns(Task.CompletedTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                    new TaskCreatedPushItemDto
                    {
                        ClientId = clientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Task with reminder",
                        ReminderAtUtc = reminderTime
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            capturedTask.Should().NotBeNull();
            capturedTask!.ReminderAtUtc.Should().Be(reminderTime);
        }

        [Fact]
        public async Task Handle_TaskCreate_ValidationFailure_ReturnsFailedWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                    new TaskCreatedPushItemDto
                    {
                        ClientId = clientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "" // Empty title should fail validation
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            taskResult.ClientId.Should().Be(clientId);
            taskResult.ServerId.Should().Be(Guid.Empty);
            taskResult.Version.Should().Be(0);
            taskResult.Status.Should().Be(SyncPushCreatedStatus.Failed);
            taskResult.Conflict.Should().NotBeNull();
            taskResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ValidationFailed);
            taskResult.Conflict.Errors.Should().NotBeEmpty();

            _taskRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        #endregion

        #region Task Update Tests

        [Fact]
        public async Task Handle_TaskUpdate_Success_ReturnsUpdatedWithNewVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var existingTask = CreateTaskItem(_userId, _now);
            SetEntityId(existingTask, taskId);
            SetEntityVersion(existingTask, 1L);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 1,
                        Date = existingTask.Date,
                        Title = "Updated Title",
                        Description = existingTask.Description
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.NewVersion.Should().BeGreaterThan(1);
            taskResult.Status.Should().Be(SyncPushUpdatedStatus.Updated);
            taskResult.Conflict.Should().BeNull();

            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TaskUpdate_NotFound_ReturnsNotFoundWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskItem?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 1,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Updated Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.NewVersion.Should().BeNull();
            taskResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            taskResult.Conflict.Should().NotBeNull();
            taskResult.Conflict!.ConflictType.Should().Be(SyncConflictType.NotFound);
        }

        [Fact]
        public async Task Handle_TaskUpdate_DeletedOnServer_ReturnsFailedWithDeletedOnServerConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var deletedTask = CreateTaskItem(_userId, _now);
            SetEntityId(deletedTask, taskId);
            deletedTask.SoftDelete(_now);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 1,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Updated Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            taskResult.Conflict.Should().NotBeNull();
            taskResult.Conflict!.ConflictType.Should().Be(SyncConflictType.DeletedOnServer);
        }

        [Fact]
        public async Task Handle_TaskUpdate_VersionMismatch_ReturnsFailedWithVersionMismatchConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var serverTask = CreateTaskItem(_userId, _now);
            SetEntityId(serverTask, taskId);
            SetEntityVersion(serverTask, 5L);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serverTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 2, // Client expects version 2, server has version 5
                        Date = serverTask.Date,
                        Title = "Client Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.NewVersion.Should().Be(5);
            taskResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            taskResult.Conflict.Should().NotBeNull();
            taskResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);
            taskResult.Conflict.ClientVersion.Should().Be(2);
            taskResult.Conflict.ServerVersion.Should().Be(5);
            taskResult.Conflict.ServerTask.Should().NotBeNull();

            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_TaskUpdate_ValidationFailure_ReturnsFailedWithValidationConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var existingTask = CreateTaskItem(_userId, _now);
            SetEntityId(existingTask, taskId);
            SetEntityVersion(existingTask, 1L);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 1,
                        Date = existingTask.Date,
                        Title = "" // Empty title should fail validation
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            taskResult.Conflict.Should().NotBeNull();
            taskResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ValidationFailed);
            taskResult.Conflict.Errors.Should().NotBeEmpty();
        }

        #endregion

        #region Task Delete Tests

        [Fact]
        public async Task Handle_TaskDelete_Success_ReturnsDeleted()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var existingTask = CreateTaskItem(_userId, _now);
            SetEntityId(existingTask, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = taskId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Deleted.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.Status.Should().Be(SyncPushDeletedStatus.Deleted);
            taskResult.Conflict.Should().BeNull();

            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_TaskDelete_NotFound_ReturnsNotFoundWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskItem?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = taskId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Deleted.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.Status.Should().Be(SyncPushDeletedStatus.NotFound);
            taskResult.Conflict.Should().BeNull(); // Delete not found is idempotent
        }

        [Fact]
        public async Task Handle_TaskDelete_AlreadyDeleted_ReturnsAlreadyDeletedWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            var deletedTask = CreateTaskItem(_userId, _now);
            SetEntityId(deletedTask, taskId);
            deletedTask.SoftDelete(_now);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = taskId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Deleted.Should().ContainSingle().Subject;
            taskResult.Id.Should().Be(taskId);
            taskResult.Status.Should().Be(SyncPushDeletedStatus.AlreadyDeleted);
            taskResult.Conflict.Should().BeNull(); // Already deleted is idempotent
        }

        #endregion

        #region Note Create Tests

        [Fact]
        public async Task Handle_NoteCreate_Success_ReturnsCreatedWithServerIdAndVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
                    {
                    new NoteCreatedPushItemDto
                    {
                        ClientId = clientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "New Note"
                        // CHANGED: Content removed - content is now in blocks
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Created.Should().ContainSingle().Subject;
            noteResult.ClientId.Should().Be(clientId);
            noteResult.ServerId.Should().NotBeEmpty();
            noteResult.Version.Should().BeGreaterThanOrEqualTo(1);
            noteResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            noteResult.Conflict.Should().BeNull();

            _noteRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<Note>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_NoteCreate_ValidationFailure_ReturnsFailedWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
                    {
                    new NoteCreatedPushItemDto
                    {
                        ClientId = clientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "" // Empty title should fail validation
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Created.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushCreatedStatus.Failed);
            noteResult.Conflict.Should().NotBeNull();
            noteResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ValidationFailed);
        }

        #endregion

        #region Note Update Tests

        [Fact]
        public async Task Handle_NoteUpdate_Success_ReturnsUpdatedWithNewVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            var existingNote = CreateNote(_userId, _now);
            SetEntityId(existingNote, noteId);
            SetEntityVersion(existingNote, 1L);

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingNote);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Updated = new[]
                    {
                    new NoteUpdatedPushItemDto
                    {
                        Id = noteId,
                        ExpectedVersion = 1,
                        Date = existingNote.Date,
                        Title = "Updated Title"
                        // CHANGED: Content removed - content is now in blocks
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Updated.Should().ContainSingle().Subject;
            noteResult.Id.Should().Be(noteId);
            noteResult.NewVersion.Should().BeGreaterThan(1);
            noteResult.Status.Should().Be(SyncPushUpdatedStatus.Updated);
            noteResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_NoteUpdate_NotFound_ReturnsNotFoundWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Note?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Updated = new[]
                    {
                    new NoteUpdatedPushItemDto
                    {
                        Id = noteId,
                        ExpectedVersion = 1,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Updated Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Updated.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            noteResult.Conflict.Should().NotBeNull();
            noteResult.Conflict!.ConflictType.Should().Be(SyncConflictType.NotFound);
        }

        [Fact]
        public async Task Handle_NoteUpdate_DeletedOnServer_ReturnsFailedWithDeletedOnServerConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            var deletedNote = CreateNote(_userId, _now);
            SetEntityId(deletedNote, noteId);
            deletedNote.SoftDelete(_now);

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedNote);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Updated = new[]
                    {
                    new NoteUpdatedPushItemDto
                    {
                        Id = noteId,
                        ExpectedVersion = 1,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Updated Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Updated.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            noteResult.Conflict.Should().NotBeNull();
            noteResult.Conflict!.ConflictType.Should().Be(SyncConflictType.DeletedOnServer);
        }

        [Fact]
        public async Task Handle_NoteUpdate_VersionMismatch_ReturnsFailedWithVersionMismatchConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            var serverNote = CreateNote(_userId, _now);
            SetEntityId(serverNote, noteId);
            SetEntityVersion(serverNote, 5L);

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serverNote);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Updated = new[]
                    {
                    new NoteUpdatedPushItemDto
                    {
                        Id = noteId,
                        ExpectedVersion = 2,
                        Date = serverNote.Date,
                        Title = "Client Title"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Updated.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            noteResult.Conflict.Should().NotBeNull();
            noteResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);
            noteResult.Conflict.ClientVersion.Should().Be(2);
            noteResult.Conflict.ServerVersion.Should().Be(5);
            noteResult.Conflict.ServerNote.Should().NotBeNull();
        }

        #endregion

        #region Note Delete Tests

        [Fact]
        public async Task Handle_NoteDelete_Success_ReturnsDeleted()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            var existingNote = CreateNote(_userId, _now);
            SetEntityId(existingNote, noteId);

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingNote);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Deleted = new[] { new NoteDeletedPushItemDto { Id = noteId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Deleted.Should().ContainSingle().Subject;
            noteResult.Id.Should().Be(noteId);
            noteResult.Status.Should().Be(SyncPushDeletedStatus.Deleted);
            noteResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_NoteDelete_NotFound_ReturnsNotFoundWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Note?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Deleted = new[] { new NoteDeletedPushItemDto { Id = noteId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Deleted.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushDeletedStatus.NotFound);
            noteResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_NoteDelete_AlreadyDeleted_ReturnsAlreadyDeletedWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var noteId = Guid.NewGuid();

            var deletedNote = CreateNote(_userId, _now);
            SetEntityId(deletedNote, noteId);
            deletedNote.SoftDelete(_now);

            _noteRepositoryMock
                .Setup(r => r.GetByIdAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedNote);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Deleted = new[] { new NoteDeletedPushItemDto { Id = noteId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Deleted.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushDeletedStatus.AlreadyDeleted);
            noteResult.Conflict.Should().BeNull();
        }

        #endregion

        #region Block Create Tests

        [Fact]
        public async Task Handle_BlockCreateTextBlock_Success_ReturnsCreatedWithServerIdAndVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();
            var parentNoteId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = clientId,
                        ParentId = parentNoteId,
                        ParentType = BlockParentType.Note,
                        Type = BlockType.Paragraph,
                        Position = "a0",
                        TextContent = "Block content"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Created.Should().ContainSingle().Subject;
            blockResult.ClientId.Should().Be(clientId);
            blockResult.ServerId.Should().NotBeEmpty();
            blockResult.Version.Should().BeGreaterThanOrEqualTo(1);
            blockResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            blockResult.Conflict.Should().BeNull();

            _blockRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<Block>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_BlockCreateAssetBlock_Success_ReturnsCreatedWithServerIdAndVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();
            var parentNoteId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = clientId,
                        ParentId = parentNoteId,
                        ParentType = BlockParentType.Note,
                        Type = BlockType.Image,
                        Position = "a0",
                        AssetClientId = "asset-123",
                        AssetFileName = "photo.jpg",
                        AssetContentType = "image/jpeg",
                        AssetSizeBytes = 1024
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Created.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            blockResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_BlockCreate_ParentNotFound_ReturnsFailedWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = clientId,
                        ParentId = null, // No parent ID
                        ParentClientId = null, // No client ID either
                        ParentType = BlockParentType.Note,
                        Type = BlockType.Paragraph,
                        Position = "a0",
                        TextContent = "Content"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Created.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushCreatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ParentNotFound);
        }

        [Fact]
        public async Task Handle_BlockCreateAssetBlock_MissingAssetMetadata_ReturnsFailedWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var clientId = Guid.NewGuid();
            var parentNoteId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = clientId,
                        ParentId = parentNoteId,
                        ParentType = BlockParentType.Note,
                        Type = BlockType.Image,
                        Position = "a0"
                        // Missing: AssetClientId, AssetFileName, AssetSizeBytes
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Created.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushCreatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ValidationFailed);
        }

        [Fact]
        public async Task Handle_BlockCreate_WithParentClientId_ResolvesFromSamePushCreate()
        {
            // Arrange
            var handler = CreateHandler();
            var noteClientId = Guid.NewGuid();
            var blockClientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
                    {
                    new NoteCreatedPushItemDto
                    {
                        ClientId = noteClientId,
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Parent Note"
                        // CHANGED: Content removed - content is now in blocks
                    }
                }
                },
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = blockClientId,
                        ParentClientId = noteClientId,
                        ParentType = BlockParentType.Note,
                        Type = BlockType.Paragraph,
                        Position = "a0",
                        TextContent = "Block content"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var noteResult = result.Value.Notes.Created.Should().ContainSingle().Subject;
            noteResult.Status.Should().Be(SyncPushCreatedStatus.Created);

            var blockResult = result.Value.Blocks.Created.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            blockResult.Conflict.Should().BeNull();
        }

        #endregion

        #region Block Update Tests

        [Fact]
        public async Task Handle_BlockUpdate_Success_ReturnsUpdatedWithNewVersion()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            var existingBlock = CreateTextBlock(_userId, _now);
            SetEntityId(existingBlock, blockId);
            SetEntityVersion(existingBlock, 1L);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Updated = new[]
                    {
                    new BlockUpdatedPushItemDto
                    {
                        Id = blockId,
                        ExpectedVersion = 1,
                        Position = "b0",
                        TextContent = "Updated content"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Updated.Should().ContainSingle().Subject;
            blockResult.Id.Should().Be(blockId);
            blockResult.NewVersion.Should().BeGreaterThan(1);
            blockResult.Status.Should().Be(SyncPushUpdatedStatus.Updated);
            blockResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_BlockUpdate_NotFound_ReturnsNotFoundWithConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Block?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Updated = new[]
                    {
                    new BlockUpdatedPushItemDto
                    {
                        Id = blockId,
                        ExpectedVersion = 1,
                        Position = "b0"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Updated.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.NotFound);
        }

        [Fact]
        public async Task Handle_BlockUpdate_BelongsToDifferentUser_ReturnsFailedWithNotFoundConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var otherUserBlock = CreateTextBlock(otherUserId, _now);
            SetEntityId(otherUserBlock, blockId);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(otherUserBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Updated = new[]
                    {
                    new BlockUpdatedPushItemDto
                    {
                        Id = blockId,
                        ExpectedVersion = 1,
                        Position = "b0"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Updated.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.NotFound);
        }

        [Fact]
        public async Task Handle_BlockUpdate_DeletedOnServer_ReturnsFailedWithDeletedOnServerConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            var deletedBlock = CreateTextBlock(_userId, _now);
            SetEntityId(deletedBlock, blockId);
            deletedBlock.SoftDelete(_now);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Updated = new[]
                    {
                    new BlockUpdatedPushItemDto
                    {
                        Id = blockId,
                        ExpectedVersion = 1,
                        Position = "b0"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Updated.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.DeletedOnServer);
        }

        [Fact]
        public async Task Handle_BlockUpdate_VersionMismatch_ReturnsFailedWithVersionMismatchConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            var serverBlock = CreateTextBlock(_userId, _now);
            SetEntityId(serverBlock, blockId);
            SetEntityVersion(serverBlock, 5L);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serverBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Updated = new[]
                    {
                    new BlockUpdatedPushItemDto
                    {
                        Id = blockId,
                        ExpectedVersion = 2,
                        Position = "b0"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Updated.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            blockResult.Conflict.Should().NotBeNull();
            blockResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);
            blockResult.Conflict.ClientVersion.Should().Be(2);
            blockResult.Conflict.ServerVersion.Should().Be(5);
            blockResult.Conflict.ServerBlock.Should().NotBeNull();
        }

        #endregion

        #region Block Delete Tests

        [Fact]
        public async Task Handle_BlockDelete_Success_ReturnsDeleted()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            var existingBlock = CreateTextBlock(_userId, _now);
            SetEntityId(existingBlock, blockId);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Deleted = new[] { new BlockDeletedPushItemDto { Id = blockId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Deleted.Should().ContainSingle().Subject;
            blockResult.Id.Should().Be(blockId);
            blockResult.Status.Should().Be(SyncPushDeletedStatus.Deleted);
            blockResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_BlockDelete_NotFound_ReturnsNotFoundWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Block?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Deleted = new[] { new BlockDeletedPushItemDto { Id = blockId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Deleted.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushDeletedStatus.NotFound);
            blockResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_BlockDelete_BelongsToDifferentUser_ReturnsNotFoundWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var otherUserBlock = CreateTextBlock(otherUserId, _now);
            SetEntityId(otherUserBlock, blockId);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(otherUserBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Deleted = new[] { new BlockDeletedPushItemDto { Id = blockId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Deleted.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushDeletedStatus.NotFound);
            blockResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_BlockDelete_AlreadyDeleted_ReturnsAlreadyDeletedWithoutConflict()
        {
            // Arrange
            var handler = CreateHandler();
            var blockId = Guid.NewGuid();

            var deletedBlock = CreateTextBlock(_userId, _now);
            SetEntityId(deletedBlock, blockId);
            deletedBlock.SoftDelete(_now);

            _blockRepositoryMock
                .Setup(r => r.GetByIdAsync(blockId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deletedBlock);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Blocks = new SyncPushBlocksDto
                {
                    Deleted = new[] { new BlockDeletedPushItemDto { Id = blockId } }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var blockResult = result.Value.Blocks.Deleted.Should().ContainSingle().Subject;
            blockResult.Status.Should().Be(SyncPushDeletedStatus.AlreadyDeleted);
            blockResult.Conflict.Should().BeNull();
        }

        #endregion

        #region Combined Scenarios Tests

        [Fact]
        public async Task Handle_MultipleEntitiesInSinglePush_ProcessesAllEntities()
        {
            // Arrange
            var handler = CreateHandler();

            var taskId = Guid.NewGuid();
            var existingTask = CreateTaskItem(_userId, _now);
            SetEntityId(existingTask, taskId);
            SetEntityVersion(existingTask, 1L);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingTask);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                    new TaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        Date = new DateOnly(2025, 1, 2),
                        Title = "New Task"
                    }
                },
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = taskId,
                        ExpectedVersion = 1,
                        Date = existingTask.Date,
                        Title = "Updated Task"
                    }
                }
                },
                Notes = new SyncPushNotesDto
                {
                    Created = new[]
                    {
                    new NoteCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        Date = new DateOnly(2025, 1, 2),
                        Title = "New Note"
                    }
                }
                },
                Blocks = new SyncPushBlocksDto
                {
                    Created = new[]
                    {
                    new BlockCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        ParentId = Guid.NewGuid(),
                        ParentType = BlockParentType.Note,  // CHANGED: Task -> Note (Tasks don't have blocks)
                        Type = BlockType.Paragraph,
                        Position = "a0",
                        TextContent = "Block content"
                    }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Tasks.Created.Should().HaveCount(1);
            result.Value.Tasks.Updated.Should().HaveCount(1);
            result.Value.Notes.Created.Should().HaveCount(1);
            result.Value.Blocks.Created.Should().HaveCount(1);

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_MixedSuccessAndFailure_ReturnsAllResults()
        {
            // Arrange
            var handler = CreateHandler();

            var existingTaskId = Guid.NewGuid();
            var existingTask = CreateTaskItem(_userId, _now);
            SetEntityId(existingTask, existingTaskId);
            SetEntityVersion(existingTask, 5L); // Version mismatch setup

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(existingTaskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingTask);

            var nonExistentTaskId = Guid.NewGuid();
            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(nonExistentTaskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskItem?)null);

            var command = new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                    new TaskCreatedPushItemDto
                    {
                        ClientId = Guid.NewGuid(),
                        Date = new DateOnly(2025, 1, 2),
                        Title = "Valid Task"
                    }
                },
                    Updated = new[]
                    {
                    new TaskUpdatedPushItemDto
                    {
                        Id = existingTaskId,
                        ExpectedVersion = 1, // Mismatch: server has version 5
                        Date = existingTask.Date,
                        Title = "Update attempt"
                    }
                },
                    Deleted = new[]
                    {
                    new TaskDeletedPushItemDto { Id = nonExistentTaskId }
                }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var createResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            createResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            createResult.Conflict.Should().BeNull();

            var updateResult = result.Value.Tasks.Updated.Should().ContainSingle().Subject;
            updateResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            updateResult.Conflict.Should().NotBeNull();
            updateResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);

            var deleteResult = result.Value.Tasks.Deleted.Should().ContainSingle().Subject;
            deleteResult.Status.Should().Be(SyncPushDeletedStatus.NotFound);
            deleteResult.Conflict.Should().BeNull();
        }

        [Fact]
        public async Task Handle_EmptyCommand_ReturnsEmptyResults()
        {
            // Arrange
            var handler = CreateHandler();
            var command = CreateEmptyCommand();

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Tasks.Created.Should().BeEmpty();
            result.Value.Tasks.Updated.Should().BeEmpty();
            result.Value.Tasks.Deleted.Should().BeEmpty();
            result.Value.Notes.Created.Should().BeEmpty();
            result.Value.Notes.Updated.Should().BeEmpty();
            result.Value.Notes.Deleted.Should().BeEmpty();
            result.Value.Blocks.Created.Should().BeEmpty();
            result.Value.Blocks.Updated.Should().BeEmpty();
            result.Value.Blocks.Deleted.Should().BeEmpty();

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Helper Methods

        private SyncPushCommand CreateEmptyCommand()
        {
            return new SyncPushCommand
            {
                DeviceId = _deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto(),
                Notes = new SyncPushNotesDto(),
                Blocks = new SyncPushBlocksDto()
            };
        }

        private static TaskItem CreateTaskItem(Guid userId, DateTime utcNow)
        {
            var createResult = TaskItem.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Test Task",
                "Description",
                null,
                null,
                null,
                null,
                utcNow);

            createResult.IsSuccess.Should().BeTrue();
            return createResult.Value;
        }

        private static Note CreateNote(Guid userId, DateTime utcNow)
        {
            // CHANGED: content parameter removed from Note.Create
            var createResult = Note.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Test Note",
                null,
                null,
                utcNow);

            createResult.IsSuccess.Should().BeTrue();
            return createResult.Value;
        }

        private static Block CreateTextBlock(Guid userId, DateTime utcNow)
        {
            var createResult = Block.CreateTextBlock(
                userId,
                Guid.NewGuid(),
                BlockParentType.Note,
                BlockType.Paragraph,
                "a0",
                "Test content",
                utcNow);

            createResult.IsSuccess.Should().BeTrue();
            return createResult.Value!;
        }

        private static void SetEntityId<T>(T entity, Guid id) where T : class
        {
            typeof(T).GetProperty("Id")!.SetValue(entity, id);
        }

        private static void SetEntityVersion<T>(T entity, long version) where T : class
        {
            typeof(T).GetProperty("Version")!.SetValue(entity, version);
        }

        #endregion
    }
}
