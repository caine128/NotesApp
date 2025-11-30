using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Commands.SetTaskCompletion;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Time;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    public sealed class SetTaskCompletionCommandHandlerTests
    {
        [Fact]
        public async Task Handle_marks_incomplete_task_as_completed_and_emits_outbox_message()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<SetTaskCompletionCommandHandler>();

            // Seed an incomplete task
            var createResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Task",
                description: "Desc",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "Office",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var task = createResult.Value!;

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();

            var handler = new SetTaskCompletionCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new SetTaskCompletionCommand(
                TaskId: task.Id,
                IsCompleted: true);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.Should().NotBeNull();
            dto.TaskId.Should().Be(task.Id);
            dto.IsCompleted.Should().BeTrue();

            // Verify DB state
            var persisted = await context.Tasks.AsNoTracking()
                .SingleAsync(t => t.Id == task.Id, CancellationToken.None);

            persisted.IsCompleted.Should().BeTrue();

            // Verify Outbox message
            var outbox = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == task.Id && o.UserId == userId, CancellationToken.None);

            outbox.AggregateType.Should().Be(nameof(TaskItem));
            outbox.MessageType.Should().Be($"{nameof(TaskItem)}.{TaskEventType.CompletionChanged}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
            outbox.AttemptCount.Should().Be(0);
        }

        [Fact]
        public async Task Handle_marks_completed_task_as_pending_and_emits_outbox_message()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<SetTaskCompletionCommandHandler>();

            // Seed a completed task
            var createResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Completed task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var task = createResult.Value!;

            // Mark completed at domain level before save
            task.MarkCompleted(DateTime.UtcNow);

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();

            var handler = new SetTaskCompletionCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new SetTaskCompletionCommand(
                TaskId: task.Id,
                IsCompleted: false);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.Should().NotBeNull();
            dto.TaskId.Should().Be(task.Id);
            dto.IsCompleted.Should().BeFalse();

            var persisted = await context.Tasks.AsNoTracking()
                .SingleAsync(t => t.Id == task.Id, CancellationToken.None);

            persisted.IsCompleted.Should().BeFalse();

            var outbox = await context.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == task.Id && o.UserId == userId, CancellationToken.None);

            outbox.AggregateType.Should().Be(nameof(TaskItem));
            outbox.MessageType.Should().Be($"{nameof(TaskItem)}.{TaskEventType.CompletionChanged}");
        }

        [Fact]
        public async Task Handle_when_task_does_not_exist_returns_not_found_and_does_not_emit_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<SetTaskCompletionCommandHandler>();

            var handler = new SetTaskCompletionCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new SetTaskCompletionCommand(
                TaskId: Guid.NewGuid(),
                IsCompleted: true);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            (await context.Tasks.ToListAsync()).Should().BeEmpty();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_task_belongs_to_another_user_returns_not_found_and_does_not_emit_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var currentUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(currentUserId);

            var logger = new LoggerFactory().CreateLogger<SetTaskCompletionCommandHandler>();

            // Seed a task for another user
            var createResult = TaskItem.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var otherTask = createResult.Value!;

            await context.Tasks.AddAsync(otherTask);
            await context.SaveChangesAsync();

            var handler = new SetTaskCompletionCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new SetTaskCompletionCommand(
                TaskId: otherTask.Id,
                IsCompleted: true);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            var persisted = await context.Tasks.AsNoTracking()
                .SingleAsync(t => t.Id == otherTask.Id, CancellationToken.None);

            persisted.IsCompleted.Should().BeFalse();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_task_is_deleted_returns_failure_and_does_not_emit_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<SetTaskCompletionCommandHandler>();

            var createResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var task = createResult.Value!;

            // Soft-delete the task before calling the handler
            task.SoftDelete(DateTime.UtcNow);

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();

            var handler = new SetTaskCompletionCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new SetTaskCompletionCommand(
                TaskId: task.Id,
                IsCompleted: true);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();

            // Task should still be deleted and not toggled
            var persisted = await context.Tasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(t => t.Id == task.Id, CancellationToken.None);

            persisted.IsDeleted.Should().BeTrue();
            persisted.IsCompleted.Should().BeFalse();

            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }
    }
}
