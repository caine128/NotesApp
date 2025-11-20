using FluentAssertions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Tasks
{
    /// <summary>
    /// Tests for TaskRepository using an in-memory SQLite AppDbContext.
    /// </summary>
    public sealed class TaskRepositoryTests
    {
        [Fact]
        public async Task GetForDayAsync_returns_only_tasks_for_given_user_and_date()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository repository = new TaskRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);

            // Seed some tasks
            var task1 = TaskItem.Create(userId, date, "Task 1", utcNow: DateTime.UtcNow).Value;
            var task2 = TaskItem.Create(userId, date, "Task 2", utcNow: DateTime.UtcNow).Value;
            var taskForOtherUser = TaskItem.Create(otherUserId, date, "Other user task", DateTime.UtcNow).Value;
            var taskForOtherDate = TaskItem.Create(userId, date.AddDays(1), "Other date task", DateTime.UtcNow).Value;

            await context.Tasks.AddRangeAsync(task1, task2, taskForOtherUser, taskForOtherDate);
            await context.SaveChangesAsync();

            // Act
            var result = await repository.GetForDayAsync(userId, date, CancellationToken.None);

            // Assert
            result.Should().HaveCount(2);
            result.Select(t => t.Title).Should().BeEquivalentTo("Task 1", "Task 2");
        }
    }
}
