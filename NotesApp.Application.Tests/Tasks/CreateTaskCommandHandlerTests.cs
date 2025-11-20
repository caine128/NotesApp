using FluentAssertions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    /// <summary>
    /// End-to-end test for CreateTaskCommandHandler using:
    /// - SQLite in-memory AppDbContext
    /// - real TaskRepository
    /// - real UnitOfWork
    /// - real SystemClock (or a fake, if you prefer)
    /// </summary>
    public sealed class CreateTaskCommandHandlerTests
    {
        [Fact]
        public async Task Handle_creates_task_and_persists_it()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock(); // or use a fake clock for deterministic times

            var handler = new CreateTaskCommandHandler(taskRepository, unitOfWork, clock);

            var userId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);

            var command = new CreateTaskCommand
            {
                UserId= userId,
                Date= date,
                Title= "My new task",
                ReminderAtUtc= DateTime.UtcNow.AddHours(1)
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Title.Should().Be("My new task");
            dto.UserId.Should().Be(userId);

            // And verify it really hit the database
            var persisted = await context.Tasks
                         .AsNoTracking() // optional but good practice in tests
                         .FirstOrDefaultAsync(t => t.Id == dto.TaskId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be("My new task");
            persisted.UserId.Should().Be(userId);
        }
    }
}
