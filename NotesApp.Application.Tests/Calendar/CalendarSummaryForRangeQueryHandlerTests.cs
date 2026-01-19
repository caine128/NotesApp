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
    public sealed class CalendarSummaryForRangeQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_calendar_summaries_for_each_day_in_range()
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

            var start = new DateOnly(2025, 2, 20);
            var day1 = start;
            var day2 = start.AddDays(1);
            var day3 = start.AddDays(2);
            var endExclusive = start.AddDays(3); // [day1, day2, day3)

            // Seed tasks (only for some days, plus other user)
            var t1 = TaskItem.Create(
                userId, day1, "Task day1", null, null, null, null, null, DateTime.UtcNow).Value;
            var t2 = TaskItem.Create(
                userId, day2, "Task day2", null, null, null, null, null, DateTime.UtcNow).Value;
            var tOtherUser = TaskItem.Create(
                otherUserId, day2, "Other user task", null, null, null, null, null, DateTime.UtcNow).Value;

            // Seed notes
            var n1 = Note.Create(userId: userId,
                                 date: day1,
                                 utcNow: DateTime.UtcNow,
                                 title: "Note day1",
                                 summary: null,
                                 tags: null).Value;

            var n3 = Note.Create(userId: userId,
                                 date: day3,
                                 utcNow: DateTime.UtcNow,
                                 title: "Note day3",
                                 summary: null,
                                 tags: null).Value;

            await context.Tasks.AddRangeAsync(t1, t2, tOtherUser);
            await context.Notes.AddRangeAsync(n1, n3);
            await context.SaveChangesAsync();

            var handler = new CalendarSummaryForRangeQueryHandler(
                taskRepository,
                noteRepository,
                currentUserServiceMock.Object);

            var query = new CalendarSummaryForRangeQuery(start, endExclusive);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var summaries = result.Value;

            // Expect 3 days in [start, endExclusive)
            summaries.Should().HaveCount(3);

            var day1Summary = summaries.Single(d => d.Date == day1);
            var day2Summary = summaries.Single(d => d.Date == day2);
            var day3Summary = summaries.Single(d => d.Date == day3);

            // Day1: 1 task (t1) + 1 note (n1)
            day1Summary.Tasks.Should().HaveCount(1);
            day1Summary.Tasks[0].Title.Should().Be("Task day1");
            day1Summary.Notes.Should().HaveCount(1);
            day1Summary.Notes[0].Title.Should().Be("Note day1");

            // Day2: only t2 for current user
            day2Summary.Tasks.Should().HaveCount(1);
            day2Summary.Tasks[0].Title.Should().Be("Task day2");
            day2Summary.Notes.Should().BeEmpty();

            // Day3: no items in range for current user (n3 is at endExclusive and thus excluded)
            day3Summary.Tasks.Should().BeEmpty();
            day3Summary.Notes
                    .Select(n => n.Title)
                    .Should().BeEquivalentTo(new[] { "Note day3" });
        }
    }
}
