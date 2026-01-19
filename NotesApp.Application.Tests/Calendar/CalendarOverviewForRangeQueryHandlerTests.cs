using FluentAssertions;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Calendar.Queries;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Calendar
{
    public sealed class CalendarOverviewForRangeQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_overview_for_each_day_in_range_respecting_user_and_range()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var start = new DateOnly(2025, 3, 10);
            var day1 = start;
            var day2 = start.AddDays(1);
            var day3 = start.AddDays(2);
            var endExclusive = start.AddDays(3); // [day1, day2, day3)

            // --- Seed tasks ---
            // In-range tasks for current user
            var t1 = TaskItem.Create(
                userId: userId,
                date: day1,
                title: "Task d1",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            var t2 = TaskItem.Create(
                userId: userId,
                date: day2,
                title: "Task d2",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            // Task for other user in same range
            var tOtherUser = TaskItem.Create(
                userId: otherUserId,
                date: day2,
                title: "Other user task",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            // Task at endExclusive boundary (should be excluded)
            var tAtEndExclusive = TaskItem.Create(
                userId: userId,
                date: endExclusive,
                title: "Task at endExclusive",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            // --- Seed notes ---
            var n1 = Note.Create(
                userId: userId,
                date: day1,
                utcNow: DateTime.UtcNow,
                title: "Note d1",
                summary: null,
                tags: null).Value;

            var n2OtherUser = Note.Create(
                userId: otherUserId,
                date: day1,
                utcNow: DateTime.UtcNow,
                title: "Other user note",
                summary: null,
                tags: null).Value;

            var n3OutsideRange = Note.Create(
                userId: userId,
                date: endExclusive,
                utcNow: DateTime.UtcNow,
                title: "Note at endExclusive",
                summary: null,
                tags: null).Value;

            await context.Tasks.AddRangeAsync(t1, t2, tOtherUser, tAtEndExclusive);
            await context.Notes.AddRangeAsync(n1, n2OtherUser, n3OutsideRange);
            await context.SaveChangesAsync();

            var handler = new CalendarOverviewForRangeQueryHandler(
                taskRepository,
                noteRepository,
                currentUserServiceMock.Object);

            var query = new CalendarOverviewForRangeQuery(
                Start: start,
                EndExclusive: endExclusive);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var overviewList = result.Value;

            // Expect an entry for day1, day2, day3 (3 days in range)
            overviewList.Should().HaveCount(3);

            var d1Overview = overviewList.Single(d => d.Date == day1);
            var d2Overview = overviewList.Single(d => d.Date == day2);
            var d3Overview = overviewList.Single(d => d.Date == day3);

            // Day1: 1 task (t1) + 1 note (n1) for current user
            d1Overview.Tasks.Should().HaveCount(1);
            d1Overview.Tasks[0].Title.Should().Be("Task d1");
            d1Overview.Notes.Should().HaveCount(1);
            d1Overview.Notes[0].Title.Should().Be("Note d1");

            // Day2: 1 task (t2) for current user, no notes
            d2Overview.Tasks.Should().HaveCount(1);
            d2Overview.Tasks[0].Title.Should().Be("Task d2");
            d2Overview.Notes.Should().BeEmpty();

            // Day3: should be empty, because we seeded a task/note at endExclusive (excluded)
            d3Overview.Tasks.Should().BeEmpty();
            d3Overview.Notes.Should().BeEmpty();
        }
    }
}
