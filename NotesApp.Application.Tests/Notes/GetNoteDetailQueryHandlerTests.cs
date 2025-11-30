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
    public sealed class GetNoteDetailQueryHandlerTests
    {
        [Fact]
        public async Task Handle_returns_note_detail_for_current_user()
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

            var myNoteResult = Note.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "My note",
                content: "My content",
                summary: "summary",
                tags: "tag1",
                utcNow: DateTime.UtcNow);
            myNoteResult.IsSuccess.Should().BeTrue();
            var myNote = myNoteResult.Value!;

            var otherNoteResult = Note.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 21),
                title: "Other note",
                content: "C",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);
            otherNoteResult.IsSuccess.Should().BeTrue();
            var otherNote = otherNoteResult.Value!;

            await context.Notes.AddRangeAsync(myNote, otherNote);
            await context.SaveChangesAsync();

            var handler = new GetNoteDetailQueryHandler(noteRepository, currentUserMock.Object);

            var query = new GetNoteDetailQuery(myNote.Id);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.Should().NotBeNull();
            dto.NoteId.Should().Be(myNote.Id);
            dto.Title.Should().Be("My note");
            dto.Content.Should().Be("My content");
            dto.Date.Should().Be(new DateOnly(2025, 2, 20));
        }

        [Fact]
        public async Task Handle_when_note_does_not_exist_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var userId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var handler = new GetNoteDetailQueryHandler(noteRepository, currentUserMock.Object);

            var query = new GetNoteDetailQuery(Guid.NewGuid());

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");
        }

        [Fact]
        public async Task Handle_when_note_belongs_to_another_user_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);

            var currentUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(currentUserId);

            var otherNoteResult = Note.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users note",
                content: "C",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);
            otherNoteResult.IsSuccess.Should().BeTrue();
            var otherNote = otherNoteResult.Value!;

            await context.Notes.AddAsync(otherNote);
            await context.SaveChangesAsync();

            var handler = new GetNoteDetailQueryHandler(noteRepository, currentUserMock.Object);

            var query = new GetNoteDetailQuery(otherNote.Id);

            var result = await handler.Handle(query, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");
        }
    }
}
