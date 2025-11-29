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

    }
}
