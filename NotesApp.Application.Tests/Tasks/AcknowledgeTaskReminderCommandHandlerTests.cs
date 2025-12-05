using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Commands.AcknowledgeReminder;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    public sealed class AcknowledgeTaskReminderCommandHandlerTests
    {
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<AcknowledgeTaskReminderCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private AcknowledgeTaskReminderCommandHandler CreateHandler()
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

            return new AcknowledgeTaskReminderCommandHandler(
                _currentUserServiceMock.Object,
                _taskRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_with_valid_task_and_reminder_acknowledges_successfully()
        {
            // Arrange
            var handler = CreateHandler();
            var task = CreateTaskWithReminder(_userId, _now);
            var taskId = task.Id;
            var deviceId = Guid.NewGuid();
            var ackAt = _now.AddMinutes(1);

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var command = new AcknowledgeTaskReminderCommand
            {
                TaskId = taskId,
                DeviceId = deviceId,
                AcknowledgedAtUtc = ackAt
            };

            // Act
            Result result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            task.ReminderAcknowledgedAtUtc.Should().Be(ackAt);
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_when_task_has_no_reminder_returns_failure()
        {
            // Arrange
            var handler = CreateHandler();
            var task = CreateTaskWithoutReminder(_userId, _now);
            var taskId = task.Id;

            _taskRepositoryMock
                .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var command = new AcknowledgeTaskReminderCommand
            {
                TaskId = taskId,
                DeviceId = Guid.NewGuid(),
                AcknowledgedAtUtc = _now.AddMinutes(1)
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        private static TaskItem CreateTaskWithReminder(Guid userId, DateTime utcNow)
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
            var task = createResult.Value;

            task.SetReminder(utcNow.AddHours(1), utcNow);

            return task;
        }

        private static TaskItem CreateTaskWithoutReminder(Guid userId, DateTime utcNow)
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
