using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
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

            return new SyncPushCommandHandler(
                _currentUserServiceMock.Object,
                _taskRepositoryMock.Object,
                _noteRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_creates_single_task_and_note_successfully()
        {
            // Arrange
            var handler = CreateHandler();

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
                            Title = "Task from client",
                            Description = "Desc"
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
                            Title = "Note from client",
                            Content = "Content"
                        }
                    }
                }
            };

            // Act
            Result<SyncPushResultDto> result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;

            dto.Tasks.Created.Should().HaveCount(1);
            dto.Tasks.Created[0].Status.Should().Be("created");
            dto.Tasks.Created[0].ServerId.Should().NotBeEmpty();
            dto.Tasks.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            dto.Notes.Created.Should().HaveCount(1);
            dto.Notes.Created[0].Status.Should().Be("created");
            dto.Notes.Created[0].ServerId.Should().NotBeEmpty();
            dto.Notes.Created[0].Version.Should().BeGreaterThanOrEqualTo(1);

            dto.Conflicts.Should().BeEmpty();

            _taskRepositoryMock.Verify(r => r.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()), Times.Once);
            _noteRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Note>(), It.IsAny<CancellationToken>()), Times.Once);
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_task_update_with_version_mismatch_creates_conflict()
        {
            // Arrange
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            // Server-side task with Version = 2
            var serverTask = CreateTaskItem(_userId, _now);
            typeof(TaskItem).GetProperty(nameof(TaskItem.Id))!
                .SetValue(serverTask, taskId);
            typeof(TaskItem).GetProperty(nameof(TaskItem.Version))!
                .SetValue(serverTask, 2L);

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
                            ExpectedVersion = 1, // mismatch
                            Date = serverTask.Date,
                            Title = "Client title",
                            Description = serverTask.Description,
                            StartTime = serverTask.StartTime,
                            EndTime = serverTask.EndTime,
                            Location = serverTask.Location,
                            TravelTime = serverTask.TravelTime,
                            ReminderAtUtc = serverTask.ReminderAtUtc
                        }
                    }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;

            dto.Tasks.Updated.Should().ContainSingle(u => u.Id == taskId && u.Status == "conflict");
            dto.Conflicts.Should().ContainSingle(c =>
                c.EntityType == "task" &&
                c.EntityId == taskId &&
                c.ConflictType == "version_mismatch" &&
                c.ClientVersion == 1 &&
                c.ServerVersion == 2);

            // No outbox message and no update applied in this case
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_task_delete_not_found_returns_not_found_without_conflict()
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
                    Deleted = new[]
                    {
                        new TaskDeletedPushItemDto
                        {
                            Id = taskId
                        }
                    }
                }
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;

            dto.Tasks.Deleted.Should().ContainSingle(d => d.Id == taskId && d.Status == "not_found");
            dto.Conflicts.Should().BeEmpty();
        }

        private static TaskItem CreateTaskItem(Guid userId, DateTime utcNow)
        {
            var createResult = TaskItem.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Title",
                "Desc",
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
            var createResult = Note.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Title",
                "Content",
                null,
                null,
                utcNow);

            createResult.IsSuccess.Should().BeTrue();
            return createResult.Value;
        }
    }
}
