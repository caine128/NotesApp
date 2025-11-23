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
    /// End-to-end style test for CreateTaskCommandHandler using:
    /// - real NotesAppDbContext (SQL Server test instance)
    /// - real TaskRepository
    /// - real UnitOfWork
    /// - real SystemClock
    /// - mocked ICurrentUserService (simulates current user from JWT)
    /// </summary>
    public sealed class CreateTaskCommandHandlerTests
    {
        [Fact]
        public async Task Handle_creates_task_and_persists_it()
        {
            // Arrange: real EF Core context pointing at test database
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            // We simulate the "current user" that would normally come from JWT claims.
            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var handler = new CreateTaskCommandHandler(
                taskRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock);

            var date = new DateOnly(2025, 2, 20);

            var command = new CreateTaskCommand
            {
                Date = date,
                Title = "My new task",
                ReminderAtUtc = DateTime.UtcNow.AddHours(1)
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Title.Should().Be("My new task");
            dto.UserId.Should().Be(userId);
            dto.Date.Should().Be(date);

            // And verify it really hit the database
            var persisted = await context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == dto.TaskId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be("My new task");
            persisted.UserId.Should().Be(userId);
            persisted.Date.Should().Be(date);
        }
    }
}
