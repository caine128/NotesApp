using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Time;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    /// <summary>
    /// End-to-end-style test for CreateTaskCommandHandler using:
    /// - real NotesAppDbContext (SQL Server test instance)
    /// - real TaskRepository
    /// - real UnitOfWork
    /// - real SystemClock
    /// - mocked ICurrentUserService (simulates current user from JWT)
    /// </summary>
    public sealed class CreateTaskCommandHandlerTests
    {
        [Fact]
        public async Task Handle_creates_task_and_persists_all_main_fields()
        {
            // Arrange: real EF Core context pointing at test database
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

            var handler = new CreateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock);

            var date = new DateOnly(2025, 2, 20);
            var reminderAtUtc = DateTime.UtcNow.AddHours(1);

            var command = new CreateTaskCommand
            {
                Date = date,
                Title = "My new task",
                Description = "Meet the client and review drawings.",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 30),
                Location = "Client Office",
                TravelTime = TimeSpan.FromMinutes(30),
                ReminderAtUtc = reminderAtUtc
            };

            var before = DateTime.UtcNow;

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            var after = DateTime.UtcNow;

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Title.Should().Be(command.Title);
            dto.Description.Should().Be(command.Description);
            dto.Date.Should().Be(command.Date);
            dto.StartTime.Should().Be(command.StartTime);
            dto.EndTime.Should().Be(command.EndTime);
            dto.Location.Should().Be(command.Location);
            dto.TravelTime.Should().Be(command.TravelTime);
            dto.IsCompleted.Should().BeFalse();
            dto.ReminderAtUtc.Should().BeCloseTo(reminderAtUtc, TimeSpan.FromSeconds(1));

            dto.CreatedAtUtc.Should().BeOnOrAfter(before);
            dto.CreatedAtUtc.Should().BeOnOrBefore(after);
            dto.UpdatedAtUtc.Should().BeOnOrAfter(dto.CreatedAtUtc);

            // And verify it really hit the database with correct UserId and fields
            var persisted = await context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == dto.TaskId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be(command.Title);
            persisted.Description.Should().Be(command.Description);
            persisted.Date.Should().Be(command.Date);
            persisted.StartTime.Should().Be(command.StartTime);
            persisted.EndTime.Should().Be(command.EndTime);
            persisted.Location.Should().Be(command.Location);
            persisted.TravelTime.Should().Be(command.TravelTime);
            persisted.ReminderAtUtc.Should().BeCloseTo(reminderAtUtc, TimeSpan.FromSeconds(1));
            persisted.UserId.Should().Be(userId);
        }

        /// <summary>
        /// Edge case: empty title should cause a failure result and no task persisted.
        /// </summary>
        [Fact]
        public async Task Handle_with_empty_title_returns_failure_and_does_not_persist()
        {
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

            var handler = new CreateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock);

            var command = new CreateTaskCommand
            {
                Date = new DateOnly(2025, 2, 20),
                Title = "" // invalid
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();

            var tasksInDb = await context.Tasks.ToListAsync();
            tasksInDb.Should().BeEmpty();
        }

        /// <summary>
        /// Edge case: EndTime before StartTime should fail.
        /// </summary>
        [Fact]
        public async Task Handle_with_endtime_before_starttime_returns_failure()
        {
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

            var handler = new CreateTaskCommandHandler(
                taskRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock);

            var command = new CreateTaskCommand
            {
                Date = new DateOnly(2025, 2, 20),
                Title = "Invalid time task",
                StartTime = new TimeOnly(12, 0),
                EndTime = new TimeOnly(11, 0) // invalid, before start
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();

            var tasksInDb = await context.Tasks.ToListAsync();
            tasksInDb.Should().BeEmpty();
        }
    }
}
