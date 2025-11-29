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
    /// Application-level tests for CreateNoteCommandHandler.
    /// Uses:
    /// - real NotesAppDbContext (SQL Server test instance)
    /// - real NoteRepository + UnitOfWork + SystemClock
    /// - mocked ICurrentUserService + ILogger
    /// </summary>
    public sealed class CreateNoteCommandHandlerTests
    {
        [Fact]
        public async Task Handle_creates_note_and_persists_main_fields()
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

            var loggerMock = new Mock<ILogger<CreateNoteCommandHandler>>();

            var handler = new CreateNoteCommandHandler(
                noteRepository,
                outboxRepository,
                unitOfWork,
                currentUserServiceMock.Object,
                clock,
                loggerMock.Object);

            var date = new DateOnly(2025, 2, 20);

            var command = new CreateNoteCommand
            {
                Date = date,
                Title = "Client feedback",
                Content = "Discussed façade options.",
                // Summary / Tags may be null or later filled by AI – keep simple here
            };

            var before = DateTime.UtcNow;

            // Act
            var result = await handler.Handle(command, CancellationToken.None);

            var after = DateTime.UtcNow;

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Title.Should().Be(command.Title);
            dto.Content.Should().Be(command.Content);
            dto.Date.Should().Be(command.Date);

            // Summary/Tags likely null at creation time unless you already populate them
            dto.CreatedAtUtc.Should().BeOnOrAfter(before);
            dto.CreatedAtUtc.Should().BeOnOrBefore(after);
            dto.UpdatedAtUtc.Should().BeOnOrAfter(dto.CreatedAtUtc);

            // Verify persistence
            var persisted = await context.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == dto.NoteId, CancellationToken.None);

            persisted.Should().NotBeNull();
            persisted!.Title.Should().Be(command.Title);
            persisted.Content.Should().Be(command.Content);
            persisted.Date.Should().Be(command.Date);
            persisted.UserId.Should().Be(userId);
        }

        /// <summary>
        /// Edge case: both Title and Content empty should fail and not persist.
        /// </summary>
        [Fact]
        public async Task Handle_with_empty_title_and_content_returns_failure_and_does_not_persist()
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

            var command = new CreateNoteCommand
            {
                Date = new DateOnly(2025, 2, 20),
                Title = "",
                Content = ""
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsFailed.Should().BeTrue();

            var notesInDb = await context.Notes.ToListAsync();
            notesInDb.Should().BeEmpty();
        }
    }
}
