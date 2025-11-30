using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Commands.UpdateTask;
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
    /// <summary>
    /// Integration-style tests for UpdateTaskCommandHandler using:
    /// - real AppDbContext (SQL Server test instance)
    /// - real TaskRepository and OutboxRepository
    /// - real UnitOfWork and SystemClock
    /// - mocked ICurrentUserService
    /// </summary>
    public sealed class UpdateTaskCommandHandlerTests
    {
        [Fact]
        public async Task Handle_updates_task_and_persists_all_main_fields_and_reminder()
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

            // Seed an existing task
            var createdAt = DateTime.UtcNow.AddMinutes(-10);

            var existingTaskResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Original title",
                description: "Original description",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "Original location",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: createdAt);

            existingTaskResult.IsSuccess.Should().BeTrue();
            var existingTask = existingTaskResult.Value!;

            await context.Tasks.AddAsync(existingTask);
            await context.SaveChangesAsync();

            var newReminder = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Utc);

            var handler = new UpdateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                new LoggerFactory().CreateLogger<UpdateTaskCommandHandler>());

            var command = new UpdateTaskCommand
            {
                TaskId = existingTask.Id,
                Date = new DateOnly(2025, 2, 22),
                Title = "Updated title",
                Description = "Updated description",
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(11, 0),
                Location = "Updated location",
                TravelTime = TimeSpan.FromMinutes(30),
                ReminderAtUtc = newReminder
            };

            var before = DateTime.UtcNow;

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            var after = DateTime.UtcNow;

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.Should().NotBeNull();

            dto.TaskId.Should().Be(existingTask.Id);
            dto.Title.Should().Be(command.Title);
            dto.Description.Should().Be(command.Description);
            dto.Date.Should().Be(command.Date);
            dto.StartTime.Should().Be(command.StartTime);
            dto.EndTime.Should().Be(command.EndTime);
            dto.Location.Should().Be(command.Location);
            dto.TravelTime.Should().Be(command.TravelTime);
            dto.IsCompleted.Should().BeFalse(); // update should not flip completion
            dto.ReminderAtUtc.Should().BeCloseTo(newReminder, TimeSpan.FromSeconds(1));

            dto.CreatedAtUtc.Should().BeOnOrBefore(before); // created in the past
            dto.UpdatedAtUtc.Should().BeOnOrAfter(before);
            dto.UpdatedAtUtc.Should().BeOnOrBefore(after);

            // Verify it really updated the database
            var persisted = await context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == existingTask.Id, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be(command.Title);
            persisted.Description.Should().Be(command.Description);
            persisted.Date.Should().Be(command.Date);
            persisted.StartTime.Should().Be(command.StartTime);
            persisted.EndTime.Should().Be(command.EndTime);
            persisted.Location.Should().Be(command.Location);
            persisted.TravelTime.Should().Be(command.TravelTime);
            persisted.IsCompleted.Should().BeFalse();
            persisted.ReminderAtUtc.Should().BeCloseTo(newReminder, TimeSpan.FromSeconds(1));

            // And verify an OutboxMessage row exists for this task
            var outbox = await context.OutboxMessages
                .AsNoTracking()
                .SingleAsync(o => o.AggregateId == existingTask.Id && o.UserId == userId, CancellationToken.None);

            outbox.AggregateType.Should().Be(nameof(TaskItem));
            outbox.MessageType.Should().Be($"{nameof(TaskItem)}.{TaskEventType.Updated}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
            outbox.AttemptCount.Should().Be(0);
        }

        [Fact]
        public async Task Handle_when_task_does_not_exist_returns_not_found()
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

            var handler = new UpdateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                new LoggerFactory().CreateLogger<UpdateTaskCommandHandler>());

            var command = new UpdateTaskCommand
            {
                TaskId = Guid.NewGuid(),
                Date = new DateOnly(2025, 2, 20),
                Title = "Title",
                Description = "Desc"
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            // No tasks or outbox messages should have been created
            (await context.Tasks.ToListAsync()).Should().BeEmpty();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_task_belongs_to_another_user_returns_not_found()
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

            // Seed a task for a different user
            var taskResult = TaskItem.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            taskResult.IsSuccess.Should().BeTrue();
            var otherTask = taskResult.Value!;

            await context.Tasks.AddAsync(otherTask);
            await context.SaveChangesAsync();

            var handler = new UpdateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                new LoggerFactory().CreateLogger<UpdateTaskCommandHandler>());

            var command = new UpdateTaskCommand
            {
                TaskId = otherTask.Id,
                Date = new DateOnly(2025, 2, 21),
                Title = "Updated title"
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Tasks.NotFound");

            // Task should remain unchanged and no outbox message created
            var persisted = await context.Tasks.AsNoTracking().SingleAsync(t => t.Id == otherTask.Id);
            persisted.Title.Should().Be("Other users task");
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_domain_update_fails_does_not_change_task_or_emit_outbox()
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

            var taskResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Valid title",
                description: "Desc",
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: "Loc",
                travelTime: TimeSpan.FromMinutes(15),
                utcNow: DateTime.UtcNow);

            taskResult.IsSuccess.Should().BeTrue();
            var task = taskResult.Value!;

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();

            var handler = new UpdateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                new LoggerFactory().CreateLogger<UpdateTaskCommandHandler>());

            // Invalid: empty title and default date and end before start
            var command = new UpdateTaskCommand
            {
                TaskId = task.Id,
                Date = default,
                Title = "   ",
                Description = "Updated",
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(10, 0)
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();

            // Task in DB should remain unchanged
            var persisted = await context.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            persisted.Title.Should().Be("Valid title");
            persisted.Date.Should().Be(new DateOnly(2025, 2, 20));
            persisted.StartTime.Should().Be(new TimeOnly(9, 0));
            persisted.EndTime.Should().Be(new TimeOnly(10, 0));

            // No outbox message should have been created
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

            var taskResult = TaskItem.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Title",
                description: "Desc",
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow);

            taskResult.IsSuccess.Should().BeTrue();
            var task = taskResult.Value!;

            // Soft-delete the task before update
            task.SoftDelete(DateTime.UtcNow);

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();

            var handler = new UpdateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                new LoggerFactory().CreateLogger<UpdateTaskCommandHandler>());

            var command = new UpdateTaskCommand
            {
                TaskId = task.Id,
                Date = new DateOnly(2025, 2, 21),
                Title = "Updated title"
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();

            // Task should still be deleted and unchanged in DB
            var persisted = await context.Tasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(t => t.Id == task.Id);
            persisted.IsDeleted.Should().BeTrue();
            persisted.Title.Should().Be("Title");

            // No outbox message should have been created
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }
    }
}
