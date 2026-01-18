using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using NotesApp.Infrastructure.Time;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Notes
{
    /// <summary>
    /// Integration tests for CreateNoteCommandHandler using real SQL Server.
    /// 
    /// CHANGED: Tests updated for block-based content model.
    /// Note no longer has a Content property.
    /// </summary>
    public sealed class CreateNoteCommandHandlerTests
    {
        /// <summary>
        /// Happy path: create a note with valid Title, Summary, and Tags.
        /// Handler should persist and return the note.
        /// </summary>
        [Fact]
        public async Task Handle_with_valid_command_creates_and_returns_note()
        {
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

            var loggerMock = new Mock<ILogger<CreateNoteCommandHandler>>();

            var handler = new CreateNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                loggerMock.Object);

            var date = new DateOnly(2025, 2, 20);

            // CHANGED: Content removed from command
            var command = new CreateNoteCommand
            {
                Date = date,
                Title = "Client feedback",
                Summary = "Meeting notes summary",
                Tags = "client,feedback"
            };

            var before = DateTime.UtcNow;

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            var after = DateTime.UtcNow;

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Title.Should().Be(command.Title);
            dto.Date.Should().Be(command.Date);
            dto.Summary.Should().Be(command.Summary);
            dto.Tags.Should().Be(command.Tags);

            dto.CreatedAtUtc.Should().BeOnOrAfter(before);
            dto.CreatedAtUtc.Should().BeOnOrBefore(after);
            dto.UpdatedAtUtc.Should().BeOnOrAfter(dto.CreatedAtUtc);

            // Verify persistence
            var persisted = await context.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == dto.NoteId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be(command.Title);
            persisted.Date.Should().Be(command.Date);
            persisted.UserId.Should().Be(userId);
        }

        /// <summary>
        /// Edge case: empty Title should fail (Title is now required).
        /// </summary>
        [Fact]
        public async Task Handle_with_empty_title_returns_failure_and_does_not_persist()
        {
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

            var loggerMock = new Mock<ILogger<CreateNoteCommandHandler>>();

            var handler = new CreateNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                loggerMock.Object);

            // CHANGED: Title is now required (was: Title OR Content required)
            var command = new CreateNoteCommand
            {
                Date = new DateOnly(2025, 2, 20),
                Title = ""
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();

            var notesInDb = await context.Notes.ToListAsync();
            notesInDb.Should().BeEmpty();
        }
    }
}
