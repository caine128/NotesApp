using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Notes.Commands.DeleteNote
{
    /// <summary>
    /// Handles the DeleteNoteCommand:
    /// - Resolves the current internal user id from the token.
    /// - Loads the note WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the note belongs to the current user.
    /// - Soft-deletes the note through the domain method.
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    /// 
    /// Returns:
    /// - Result.Ok()                 -> HTTP 204 No Content
    /// - Result.Fail (Notes.NotFound)-> HTTP 404 Not Found
    /// - Other failures              -> HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteNoteCommandHandler
        : IRequestHandler<DeleteNoteCommand, Result>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteNoteCommandHandler> _logger;

        public DeleteNoteCommandHandler(
            INoteRepository noteRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteNoteCommandHandler> logger)
        {
            _noteRepository = noteRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteNoteCommand request, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the note WITHOUT tracking
            //    This ensures the soft-delete modification won't auto-persist if outbox creation fails
            var note = await _noteRepository.GetByIdUntrackedAsync(request.NoteId, cancellationToken);

            if (note is null || note.UserId != userId)
            {
                _logger.LogWarning(
                    "DeleteNote failed: note {NoteId} not found for user {UserId}.",
                    request.NoteId,
                    userId);

                return Result.Fail(
                    new Error("Note not found.")
                        .WithMetadata("ErrorCode", "Notes.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain soft delete (entity is NOT tracked, so modifications are in-memory only)
            var deleteResult = note.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return deleteResult.ToResult();
            }

            // 4) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                NoteId = note.Id,
                note.UserId,
                note.Date,
                note.Title,
                Event = NoteEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult();
            }

            // 5) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _noteRepository.Update(note);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Note {NoteId} soft-deleted for user {UserId}.",
                note.Id,
                userId);

            return Result.Ok();
        }
    }
}
