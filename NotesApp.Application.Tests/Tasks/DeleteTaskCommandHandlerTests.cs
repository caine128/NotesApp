using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Commands.DeleteTask;
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
    public sealed class DeleteTaskCommandHandlerTests
    {
        [Fact]
        public async Task Handle_soft_deletes_task_and_emits_outbox_message()
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

            var logger = new LoggerFactory().CreateLogger<DeleteTaskCommandHandler>();

            // Seed a task for this user
            var createResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Task to delete",
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

            var handler = new DeleteTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteTaskCommand
            {
                TaskId = task.Id
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            // Task should be soft-deleted in DB (use IgnoreQueryFilters to see deleted row)
            var persisted = await context.Tasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(t => t.Id == task.Id, CancellationToken.None);

            persisted.IsDeleted.Should().BeTrue();

            // Outbox message should exist
            var outbox = await context.OutboxMessages
                .AsNoTracking()
                .SingleAsync(o => o.AggregateId == task.Id && o.UserId == userId, CancellationToken.None);

            outbox.AggregateType.Should().Be(nameof(TaskItem));
            outbox.MessageType.Should().Be($"{nameof(TaskItem)}.{TaskEventType.Deleted}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
            outbox.AttemptCount.Should().Be(0);
        }

        [Fact]
        public async Task Handle_when_task_does_not_exist_returns_not_found_and_does_not_write_outbox()
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

            var logger = new LoggerFactory().CreateLogger<DeleteTaskCommandHandler>();

            var handler = new DeleteTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteTaskCommand
            {
                TaskId = Guid.NewGuid()
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            // No tasks or outbox rows should exist
            (await context.Tasks.ToListAsync()).Should().BeEmpty();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_task_belongs_to_another_user_returns_not_found_and_does_not_write_outbox()
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

            var logger = new LoggerFactory().CreateLogger<DeleteTaskCommandHandler>();

            // Seed a task for a different user
            var createResult = TaskItem.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users task",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var otherTask = createResult.Value!;

            await context.Tasks.AddAsync(otherTask);
            await context.SaveChangesAsync();

            var handler = new DeleteTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteTaskCommand
            {
                TaskId = otherTask.Id
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            // Task remains undeleted and no outbox messages should exist
            var persisted = await context.Tasks.AsNoTracking()
                .SingleAsync(t => t.Id == otherTask.Id, CancellationToken.None);

            persisted.IsDeleted.Should().BeFalse();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }
    }
}
