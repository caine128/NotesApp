using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notes
{
    /// <summary>
    /// Unit tests for UpdateNoteCommandHandler.
    /// 
    /// CHANGED: Tests updated for block-based content model.
    /// Note no longer has a Content property - Title is now required.
    /// </summary>
    public sealed class UpdateNoteCommandHandlerTests
    {
        private readonly Mock<INoteRepository> _noteRepository = new();
        private readonly Mock<IOutboxRepository> _outboxRepository = new();
        private readonly Mock<IUnitOfWork> _unitOfWork = new();
        private readonly Mock<ICurrentUserService> _currentUser = new();
        private readonly Mock<ISystemClock> _clock = new();
        private readonly Mock<ILogger<UpdateNoteCommandHandler>> _logger = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc);

        private UpdateNoteCommandHandler CreateHandler()
        {
            _currentUser.Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            _clock.Setup(x => x.UtcNow).Returns(_now);

            return new UpdateNoteCommandHandler(
                _noteRepository.Object,
                _outboxRepository.Object,
                _unitOfWork.Object,
                _currentUser.Object,
                _clock.Object,
                _logger.Object);
        }

        /// <summary>
        /// Creates a test Note entity.
        /// CHANGED: Note.Create no longer takes content parameter.
        /// </summary>
        private Note CreateNote(Guid id, bool deleted = false)
        {
            var note = Note.Create(
                _userId,
                new DateOnly(2025, 2, 20),
                "Original title",
                null,  // summary
                null,  // tags
                _now).Value!;

            typeof(Note).GetProperty("Id")!.SetValue(note, id);

            if (deleted)
            {
                note.SoftDelete(_now.AddMinutes(1));
            }

            return note;
        }

        // -------------------------------------------------------------------------
        // 1. Happy path
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_UpdatesNoteSuccessfully()
        {
            // Arrange
            var noteId = Guid.NewGuid();
            var note = CreateNote(noteId);

            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(note);

            var handler = CreateHandler();

            // CHANGED: Content removed from command
            var command = new UpdateNoteCommand
            {
                NoteId = noteId,
                Date = new DateOnly(2025, 2, 22),
                Title = "Updated title",
                Summary = "summary",
                Tags = "tag1"
            };

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value.Title.Should().Be("Updated title");

            _noteRepository.Verify(r => r.Update(note), Times.Once);
            _outboxRepository.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // -------------------------------------------------------------------------
        // 2. Note not found
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_WhenNoteDoesNotExist_ReturnsNotFound()
        {
            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Note?)null);

            var handler = CreateHandler();

            // CHANGED: Content removed from command
            var command = new UpdateNoteCommand
            {
                NoteId = Guid.NewGuid(),
                Date = new DateOnly(2025, 2, 20),
                Title = "A title"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");
        }

        // -------------------------------------------------------------------------
        // 3. Another user's note → NotFound
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_WhenNoteBelongsToAnotherUser_ReturnsNotFound()
        {
            var note = CreateNote(Guid.NewGuid());
            typeof(Note).GetProperty("UserId")!.SetValue(note, Guid.NewGuid());

            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(note);

            var handler = CreateHandler();

            // CHANGED: Content removed from command
            var command = new UpdateNoteCommand
            {
                NoteId = note.Id,
                Date = new DateOnly(2025, 2, 20),
                Title = "A title"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");
        }

        // -------------------------------------------------------------------------
        // 4. Updating deleted note → Domain failure
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_WhenNoteIsDeleted_ReturnsDomainFailure()
        {
            var noteId = Guid.NewGuid();
            var note = CreateNote(noteId, deleted: true);

            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(note);

            var handler = CreateHandler();

            // CHANGED: Content removed from command
            var command = new UpdateNoteCommand
            {
                NoteId = noteId,
                Date = new DateOnly(2025, 2, 20),
                Title = "Updated"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
        }

        // -------------------------------------------------------------------------
        // 5. Invalid Update (empty title) - CHANGED: Title is now required
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_EmptyTitle_ReturnsDomainFailure()
        {
            var noteId = Guid.NewGuid();
            var note = CreateNote(noteId);

            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(note);

            var handler = CreateHandler();

            // CHANGED: Title is now required (was: Title OR Content required)
            var command = new UpdateNoteCommand
            {
                NoteId = noteId,
                Date = new DateOnly(2025, 2, 20),
                Title = "   "
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
        }

        // -------------------------------------------------------------------------
        // 6. Default date validation
        // -------------------------------------------------------------------------
        [Fact]
        public async Task Handle_DefaultDate_ReturnsDomainFailure()
        {
            var noteId = Guid.NewGuid();
            var note = CreateNote(noteId);

            _noteRepository.Setup(r => r.GetByIdUntrackedAsync(noteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(note);

            var handler = CreateHandler();

            // CHANGED: Content removed from command
            var command = new UpdateNoteCommand
            {
                NoteId = noteId,
                Date = default,
                Title = "Updated title"
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
        }
    }
}
