using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Queries;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notes
{
    public sealed class GetNoteSummariesForDayQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_summaries_for_current_user_and_date()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<GetNoteSummariesForDayQueryHandler>();

            var date = new DateOnly(2025, 2, 20);

            // CHANGED: content parameter removed from Note.Create
            var n1 = Note.Create(userId, date, "T1", null, null, DateTime.UtcNow).Value!;
            var n2 = Note.Create(userId, date, "T2", null, null, DateTime.UtcNow).Value!;
            var otherDate = Note.Create(userId, new DateOnly(2025, 2, 21), "Other date", null, null, DateTime.UtcNow).Value!;
            var otherUser = Note.Create(otherUserId, date, "Other user", null, null, DateTime.UtcNow).Value!;

            await context.Notes.AddRangeAsync(n1, n2, otherDate, otherUser);
            await context.SaveChangesAsync();

            var handler = new GetNoteSummariesForDayQueryHandler(noteRepository, currentUserMock.Object, logger);

            var query = new GetNoteSummariesForDayQuery(date);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var list = result.Value;
            list.Should().NotBeNull();
            list.Should().HaveCount(2);

            list.Select(x => x.NoteId).Should().BeEquivalentTo(new[] { n1.Id, n2.Id });
        }

        [Fact]
        public async Task Handle_when_no_notes_for_day_returns_empty_list()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<GetNoteSummariesForDayQueryHandler>();

            var handler = new GetNoteSummariesForDayQueryHandler(noteRepository, currentUserMock.Object, logger);

            var query = new GetNoteSummariesForDayQuery(new DateOnly(2025, 2, 20));

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeEmpty();
        }
    }
}
