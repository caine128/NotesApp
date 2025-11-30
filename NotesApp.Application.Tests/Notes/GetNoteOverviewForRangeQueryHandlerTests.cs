using FluentAssertions;
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
    public sealed class GetNoteOverviewForRangeQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_overview_only_for_current_user_and_in_range_ordered_by_date()
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

            var start = new DateOnly(2025, 2, 20);
            var endExclusive = new DateOnly(2025, 2, 23);

            var n1 = Note.Create(userId, new DateOnly(2025, 2, 20), "D20", "C", null, null, DateTime.UtcNow).Value!;
            var n2 = Note.Create(userId, new DateOnly(2025, 2, 21), "D21", "C", null, null, DateTime.UtcNow).Value!;
            var n3 = Note.Create(userId, new DateOnly(2025, 2, 22), "D22", "C", null, null, DateTime.UtcNow).Value!;

            var beforeRange = Note.Create(userId, new DateOnly(2025, 2, 19), "Before", "C", null, null, DateTime.UtcNow).Value!;
            var afterRange = Note.Create(userId, new DateOnly(2025, 2, 23), "After", "C", null, null, DateTime.UtcNow).Value!;

            var otherUserInRange = Note.Create(otherUserId, new DateOnly(2025, 2, 21), "Other", "C", null, null, DateTime.UtcNow).Value!;

            await context.Notes.AddRangeAsync(n1, n2, n3, beforeRange, afterRange, otherUserInRange);
            await context.SaveChangesAsync();

            var handler = new GetNoteOverviewForRangeQueryHandler(noteRepository, currentUserMock.Object);

            var query = new GetNoteOverviewForRangeQuery(start, endExclusive);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var list = result.Value;
            list.Should().NotBeNull();
            list.Should().HaveCount(3);

            list.Select(x => x.Date).Should().ContainInOrder(
                new DateOnly(2025, 2, 20),
                new DateOnly(2025, 2, 21),
                new DateOnly(2025, 2, 22));
        }

        [Fact]
        public async Task Handle_when_no_notes_in_range_returns_empty_list()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var handler = new GetNoteOverviewForRangeQueryHandler(noteRepository, currentUserMock.Object);

            var query = new GetNoteOverviewForRangeQuery(
                Start: new DateOnly(2025, 2, 20),
                EndExclusive: new DateOnly(2025, 2, 21));

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Should().BeEmpty();
        }
    }
}
