using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Commands.ResolveConflicts;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Unit tests for ResolveSyncConflictsCommandHandler.
    /// 
    /// CHANGED: Handler now requires IBlockRepository for block conflict resolution.
    /// </summary>
    public sealed class ResolveSyncConflictsCommandHandlerTests
    {
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<INoteRepository> _noteRepositoryMock = new();
        private readonly Mock<IBlockRepository> _blockRepositoryMock = new();  // ADDED
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<ResolveSyncConflictsCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private ResolveSyncConflictsCommandHandler CreateHandler()
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

            // CHANGED: Added blockRepository parameter
            return new ResolveSyncConflictsCommandHandler(
                _currentUserServiceMock.Object,
                _taskRepositoryMock.Object,
                _noteRepositoryMock.Object,
                _blockRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_keep_server_for_task_does_not_modify_entity()
        {
            // Arrange
            var handler = CreateHandler();
            var task = CreateTaskItem(_userId, _now);
            var taskId = task.Id;

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var request = new ResolveSyncConflictsRequestDto
            {
                Resolutions = new[]
                {
                    new SyncConflictResolutionDto
                    {
                        EntityType = SyncEntityType.Task,
                        EntityId = taskId,
                        Choice = SyncResolutionChoice.KeepServer,
                        ExpectedVersion = task.Version
                    }
                }
            };

            var command = new ResolveSyncConflictsCommand { Request = request };

            // Act
            Result<ResolveSyncConflictsResultDto> result =
                await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Results.Should().ContainSingle(r =>
                r.EntityType == SyncEntityType.Task &&
                r.EntityId == taskId &&
                r.Status == SyncConflictResolutionStatus.KeptServer &&
                r.NewVersion == task.Version);

            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_keep_client_for_task_updates_entity_when_versions_match()
        {
            // Arrange
            var handler = CreateHandler();
            var task = CreateTaskItem(_userId, _now);
            var taskId = task.Id;
            var originalVersion = task.Version;

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var request = new ResolveSyncConflictsRequestDto
            {
                Resolutions = new[]
                {
                    new SyncConflictResolutionDto
                    {
                        EntityType = SyncEntityType.Task,
                        EntityId = taskId,
                        Choice = SyncResolutionChoice.KeepClient,
                        ExpectedVersion = originalVersion,
                        TaskData = new TaskConflictResolutionDataDto
                        {
                            Date = task.Date,
                            Title = "Client final title",
                            Description = task.Description,
                            StartTime = task.StartTime,
                            EndTime = task.EndTime,
                            Location = task.Location,
                            TravelTime = task.TravelTime,
                            ReminderAtUtc = task.ReminderAtUtc
                        }
                    }
                }
            };

            var command = new ResolveSyncConflictsCommand { Request = request };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Results.Should().ContainSingle(r =>
                r.EntityType == SyncEntityType.Task &&
                r.EntityId == taskId &&
                r.Status == SyncConflictResolutionStatus.Updated &&
                r.NewVersion.HasValue &&
                r.NewVersion.Value > originalVersion);

            task.Title.Should().Be("Client final title");
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_keep_client_for_task_with_version_mismatch_returns_conflict()
        {
            // Arrange
            var handler = CreateHandler();
            var task = CreateTaskItem(_userId, _now);
            var taskId = task.Id;

            typeof(TaskItem).GetProperty(nameof(TaskItem.Version))!
                .SetValue(task, 5L);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var request = new ResolveSyncConflictsRequestDto
            {
                Resolutions = new[]
                {
                    new SyncConflictResolutionDto
                    {
                        EntityType = SyncEntityType.Task,
                        EntityId = taskId,
                        Choice = SyncResolutionChoice.KeepClient,
                        ExpectedVersion = 3, // wrong
                        TaskData = new TaskConflictResolutionDataDto
                        {
                            Date = task.Date,
                            Title = "Client final title",
                            Description = task.Description,
                            StartTime = task.StartTime,
                            EndTime = task.EndTime,
                            Location = task.Location,
                            TravelTime = task.TravelTime,
                            ReminderAtUtc = task.ReminderAtUtc
                        }
                    }
                }
            };

            var command = new ResolveSyncConflictsCommand { Request = request };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Results.Should().ContainSingle(r =>
                r.EntityType == SyncEntityType.Task &&
                r.EntityId == taskId &&
                r.Status == SyncConflictResolutionStatus.Conflict &&
                r.NewVersion == 5);

            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
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
    }
}
