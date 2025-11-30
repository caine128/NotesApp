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
    ///  Tests for TaskRepository using a SQL Server test AppDbContext.
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
            var task1 = TaskItem.Create(userId: userId,
                                        date: date,
                                        title: "Task 1",
                                        description: null,
                                        startTime: null,
                                        endTime: null,
                                        location: null,
                                        travelTime: null,
                                        utcNow: DateTime.UtcNow).Value;

            var task2 = TaskItem.Create(userId: userId,
                                        date: date,
                                        title: "Task 2",
                                        description: null,
                                        startTime: null,
                                        endTime: null,
                                        location: null,
                                        travelTime: null,
                                        utcNow: DateTime.UtcNow).Value;

            var taskForOtherUser = TaskItem.Create(userId: otherUserId,
                                                   date: date,
                                                   title: "Other user task",
                                                   description: null,
                                                   startTime: null,
                                                   endTime: null,
                                                   location: null,
                                                   travelTime: null,
                                                   utcNow: DateTime.UtcNow).Value;

            var taskForOtherDate = TaskItem.Create(userId: userId,
                                                   date: date.AddDays(1),
                                                   title: "Other date task",
                                                   description: null,
                                                   startTime: null,
                                                   endTime: null,
                                                   location: null,
                                                   travelTime: null,
                                                   utcNow: DateTime.UtcNow).Value;

            await context.Tasks.AddRangeAsync(task1, task2, taskForOtherUser, taskForOtherDate);
            await context.SaveChangesAsync();

            // Act
            var result = await repository.GetForDayAsync(userId, date, CancellationToken.None);

            // Assert
            result.Should().HaveCount(2);
            result.Select(t => t.Title).Should().BeEquivalentTo("Task 1", "Task 2");
        }

        [Fact]
        public async Task GetForDateRangeAsync_respects_user_and_range_boundaries()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository repository = new TaskRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var day0 = new DateOnly(2025, 2, 20);
            var day1 = day0.AddDays(1);
            var day2 = day0.AddDays(2);
            var day3 = day0.AddDays(3);

            // In range: day0, day1, day2 (endExclusive = day3)
            var inRangeTask1 = TaskItem.Create(
                userId, day0, "In range 1", null, null, null, null, null, DateTime.UtcNow).Value;
            var inRangeTask2 = TaskItem.Create(
                userId, day1, "In range 2", null, null, null, null, null, DateTime.UtcNow).Value;
            var inRangeTask3 = TaskItem.Create(
                userId, day2, "In range 3", null, null, null, null, null, DateTime.UtcNow).Value;

            // Outside range: endExclusive boundary and before start
            var beforeRange = TaskItem.Create(
                userId, day0.AddDays(-1), "Before range", null, null, null, null, null, DateTime.UtcNow).Value;
            var atEndExclusive = TaskItem.Create(
                userId, day3, "At endExclusive", null, null, null, null, null, DateTime.UtcNow).Value;

            // Same range dates but different user
            var otherUserTask = TaskItem.Create(
                otherUserId, day1, "Other user in range", null, null, null, null, null, DateTime.UtcNow).Value;

            await context.Tasks.AddRangeAsync(
                inRangeTask1, inRangeTask2, inRangeTask3,
                beforeRange, atEndExclusive, otherUserTask);
            await context.SaveChangesAsync();

            var result = await repository.GetForDateRangeAsync(
                userId, day0, day3, CancellationToken.None);

            result.Select(t => t.Title).Should().BeEquivalentTo("In range 1", "In range 2", "In range 3");
        }
    }
}
