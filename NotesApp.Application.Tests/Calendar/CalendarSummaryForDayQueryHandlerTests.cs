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
    public sealed class CalendarSummaryForDayQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_summaries_for_tasks_and_notes_for_given_day()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            ITaskRepository taskRepository = new TaskRepository(context);
            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var date = new DateOnly(2025, 2, 20);

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            // Seed tasks for user and other user
            var userTask = TaskItem.Create(
                userId,
                date,
                "User task",
                description: null,
                startTime: new TimeOnly(9, 0),
                endTime: new TimeOnly(10, 0),
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            var otherUserTask = TaskItem.Create(
                otherUserId,
                date,
                "Other user task",
                description: null,
                startTime: new TimeOnly(11, 0),
                endTime: new TimeOnly(12, 0),
                location: null,
                travelTime: null,
                utcNow: DateTime.UtcNow).Value;

            // Seed notes for user and other user
            var userNote = Note.Create(
                userId: userId,
                date: date,
                utcNow: DateTime.UtcNow,
                title: "User note",
                content: "Content",
                summary: null,
                tags: null).Value;

            var otherUserNote = Note.Create(
                userId: otherUserId,
                date: date,
                utcNow: DateTime.UtcNow,
                title: "Other user note",
                content: "Secret content",
                summary: null,
                tags: null).Value;

            await context.Tasks.AddRangeAsync(userTask, otherUserTask);
            await context.Notes.AddRangeAsync(userNote, otherUserNote);
            await context.SaveChangesAsync();

            var handler = new CalendarSummaryForDayQueryHandler(
                taskRepository,
                noteRepository,
                currentUserServiceMock.Object);

            var query = new CalendarSummaryForDayQuery(date);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;
            dto.Date.Should().Be(date);

            dto.Tasks.Should().HaveCount(1);
            dto.Tasks[0].Title.Should().Be("User task");

            dto.Notes.Should().HaveCount(1);
            dto.Notes[0].Title.Should().Be("User note");
        }
    }
}
