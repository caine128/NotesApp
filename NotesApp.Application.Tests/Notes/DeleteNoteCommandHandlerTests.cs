using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Notes.Commands.DeleteNote;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Time;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notes
{
    public class DeleteNoteCommandHandlerTests
    {
        [Fact]
        public async Task Handle_deletes_note_and_emits_outbox_deleted_message()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var loggerMock = new Mock<ILogger<DeleteNoteCommandHandler>>();

            var userId = Guid.NewGuid();
            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            // Seed a note first (using Note.Create directly, or through handler)
            var utcNow = clock.UtcNow;
            var domainNoteResult = Note.Create(
                userId,
                new DateOnly(2025, 2, 20),
                "Title",
                "Content",
                null,
                string.Empty,
                utcNow);
            var note = domainNoteResult.Value!;
            await noteRepository.AddAsync(note, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            var handler = new DeleteNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                loggerMock.Object);

            var command = new DeleteNoteCommand(note.Id);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert success
            result.IsSuccess.Should().BeTrue();

            // Assert note is soft-deleted
            // Assert note is soft-deleted
            var persistedNote = await context.Notes
                .IgnoreQueryFilters()  // <-- ADD THIS
                .AsNoTracking()
                .SingleAsync(n => n.Id == note.Id);
            persistedNote.IsDeleted.Should().BeTrue();

            // Assert outbox message exists (OutboxMessages doesn't have a query filter, but being explicit doesn't hurt)
            var outbox = await context.OutboxMessages
                .AsNoTracking()
                .SingleAsync(o => o.AggregateId == note.Id && o.UserId == userId);

            outbox.AggregateType.Should().Be(nameof(Note));
            outbox.MessageType.Should().Be($"{nameof(Note)}.{NoteEventType.Deleted}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Handle_when_note_does_not_exist_returns_not_found_and_does_not_write_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<DeleteNoteCommandHandler>();

            var handler = new DeleteNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteNoteCommand(Guid.NewGuid());


            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");

            (await context.Notes.ToListAsync()).Should().BeEmpty();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_note_belongs_to_another_user_returns_not_found_and_does_not_write_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var currentUserId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(currentUserId);

            var logger = new LoggerFactory().CreateLogger<DeleteNoteCommandHandler>();

            // Seed a note for a different user
            var createResult = Note.Create(
                userId: otherUserId,
                date: new DateOnly(2025, 2, 20),
                title: "Other users note",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var otherNote = createResult.Value!;

            await context.Notes.AddAsync(otherNote);
            await context.SaveChangesAsync();

            var handler = new DeleteNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteNoteCommand(otherNote.Id);


            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
            result.Errors[0].Metadata["ErrorCode"].Should().Be("Notes.NotFound");

            var persisted = await context.Notes.AsNoTracking()
                .SingleAsync(n => n.Id == otherNote.Id, CancellationToken.None);

            persisted.IsDeleted.Should().BeFalse();
            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_when_note_is_already_deleted_returns_failure_and_does_not_emit_additional_outbox()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            INoteRepository noteRepository = new NoteRepository(context);
            IOutboxRepository outboxRepository = new OutboxRepository(context);
            IUnitOfWork unitOfWork = new UnitOfWork(context);
            ISystemClock clock = new SystemClock();

            var userId = Guid.NewGuid();

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(userId);

            var logger = new LoggerFactory().CreateLogger<DeleteNoteCommandHandler>();

            // Seed a deleted note
            var createResult = Note.Create(
                userId: userId,
                date: new DateOnly(2025, 2, 20),
                title: "Note",
                content: "Content",
                summary: null,
                tags: null,
                utcNow: DateTime.UtcNow);

            createResult.IsSuccess.Should().BeTrue();
            var note = createResult.Value!;

            note.SoftDelete(DateTime.UtcNow);

            await context.Notes.AddAsync(note);
            await context.SaveChangesAsync();

            var handler = new DeleteNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                logger);

            var command = new DeleteNoteCommand(note.Id);

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();

            var persisted = await context.Notes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(n => n.Id == note.Id, CancellationToken.None);

            persisted.IsDeleted.Should().BeTrue();

            (await context.OutboxMessages.ToListAsync()).Should().BeEmpty();
        }

    }
}
