using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Sync.Queries;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Sync
{
    public sealed class GetSyncChangesQueryHandlerTests
    {
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<INoteRepository> _noteRepositoryMock = new();
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ILogger<GetSyncChangesQueryHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();

        private GetSyncChangesQueryHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            return new GetSyncChangesQueryHandler(
                _taskRepositoryMock.Object,
                _noteRepositoryMock.Object,
                _deviceRepositoryMock.Object,
                _currentUserServiceMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_with_non_owned_device_returns_DeviceNotFound_error()
        {
            // Arrange
            var handler = CreateHandler();
            var otherUserId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();

            // Device belongs to someone else
            var foreignDevice = UserDevice.Create(
                otherUserId,
                "token-123",
                DevicePlatform.Android,
                "Other device",
                DateTime.UtcNow).Value!;

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignDevice);

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: deviceId, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        [Fact]
        public async Task Handle_initial_sync_treats_all_non_deleted_items_as_created()
        {
            // Arrange
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var task = CreateTask(_userId, now, isDeleted: false);
            var note = CreateNote(_userId, now, isDeleted: false);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskItem> { task });

            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Note> { note });

            var handler = CreateHandler();
            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null,MaxItemsPerEntity:null);

            // Act
            Result<SyncChangesDto> result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;

            dto.Tasks.Created.Should().HaveCount(1);
            dto.Tasks.Updated.Should().BeEmpty();
            dto.Tasks.Deleted.Should().BeEmpty();

            dto.Notes.Created.Should().HaveCount(1);
            dto.Notes.Updated.Should().BeEmpty();
            dto.Notes.Deleted.Should().BeEmpty();

            dto.Tasks.Created[0].Id.Should().Be(task.Id);
            dto.Notes.Created[0].Id.Should().Be(note.Id);

            dto.ServerTimestampUtc.Should().NotBe(default);
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_created_updated_and_deleted_correctly()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var createdTask = CreateTask(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedTask = CreateTask(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedTask = CreateTask(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            var createdNote = CreateNote(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedNote = CreateNote(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedNote = CreateNote(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskItem> { createdTask, updatedTask, deletedTask });

            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Note> { createdNote, updatedNote, deletedNote });

            var handler = CreateHandler();
            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity:null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Tasks.Created.Should().ContainSingle(t => t.Id == createdTask.Id);
            dto.Tasks.Updated.Should().ContainSingle(t => t.Id == updatedTask.Id);
            dto.Tasks.Deleted.Should().ContainSingle(t => t.Id == deletedTask.Id);

            dto.Notes.Created.Should().ContainSingle(n => n.Id == createdNote.Id);
            dto.Notes.Updated.Should().ContainSingle(n => n.Id == updatedNote.Id);
            dto.Notes.Deleted.Should().ContainSingle(n => n.Id == deletedNote.Id);

            dto.Tasks.Deleted[0].DeletedAtUtc.Should().Be(deletedTask.UpdatedAtUtc);
            dto.Notes.Deleted[0].DeletedAtUtc.Should().Be(deletedNote.UpdatedAtUtc);
        }

        private static TaskItem CreateTask(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateTask(userId, createdAt, createdAt, isDeleted);
        }

        private static TaskItem CreateTask(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            // Use factory to respect invariants, then tweak timestamps/state.
            var result = TaskItem.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Task",
                "Desc",
                null,
                null,
                null,
                null,
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            typeof(TaskItem).GetProperty(nameof(TaskItem.CreatedAtUtc))!
                .SetValue(task, createdAt);

            typeof(TaskItem).GetProperty(nameof(TaskItem.UpdatedAtUtc))!
                .SetValue(task, updatedAt);

            if (isDeleted)
            {
                typeof(TaskItem).GetProperty(nameof(TaskItem.IsDeleted))!
                    .SetValue(task, true);
            }

            return task;
        }

        private static Note CreateNote(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateNote(userId, createdAt, createdAt, isDeleted);
        }

        private static Note CreateNote(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            var result = Note.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Title",
                "Content",
                null,
                null,
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            typeof(Note).GetProperty(nameof(Note.CreatedAtUtc))!
                .SetValue(note, createdAt);

            typeof(Note).GetProperty(nameof(Note.UpdatedAtUtc))!
                .SetValue(note, updatedAt);

            if (isDeleted)
            {
                typeof(Note).GetProperty(nameof(Note.IsDeleted))!
                    .SetValue(note, true);
            }

            return note;
        }
    }
}
