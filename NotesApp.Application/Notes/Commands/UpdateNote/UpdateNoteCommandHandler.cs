using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Notes.Commands.UpdateNote
{
    public sealed class UpdateNoteCommandHandler
    : IRequestHandler<UpdateNoteCommand, Result<NoteDetailDto>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateNoteCommandHandler> _logger;

        public UpdateNoteCommandHandler(INoteRepository noteRepository,
                                        IOutboxRepository outboxRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock,
                                        ILogger<UpdateNoteCommandHandler> logger)
        {
            _noteRepository = noteRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<NoteDetailDto>> Handle(UpdateNoteCommand command,
                                                        CancellationToken cancellationToken)
        {
            // 1) Resolve current user
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the note WITHOUT tracking
            //    This ensures modifications won't auto-persist if we return early due to failures
            var note = await _noteRepository.GetByIdUntrackedAsync(command.NoteId, cancellationToken);

            if (note is null || note.UserId != userId)
            {
                _logger.LogWarning(
                    "UpdateNote failed: note {NoteId} not found for user {UserId}.",
                    command.NoteId,
                    userId);

                return Result.Fail<NoteDetailDto>(
                    new Error("Note not found.")
                        .WithMetadata("ErrorCode", "Notes.NotFound"));
            }

            if (note.IsDeleted)
            {
                return Result.Fail<NoteDetailDto>(
                    new Error("Cannot update a deleted note.")
                        .WithMetadata("ErrorCode", "Notes.Deleted"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain update (entity is NOT tracked, so modifications are in-memory only)
            var updateResult = note.Update(title: command.Title,
                                           content: command.Content,
                                           summary: command.Summary,
                                           tags: command.Tags,
                                           date: command.Date,
                                           utcNow: utcNow);

            if (updateResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return updateResult.ToResult(() => note.ToDetailDto());
            }

            // 4) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                NoteId = note.Id,
                note.UserId,
                note.Date,
                note.Title,
                note.Content,
                note.Summary,
                note.Tags,
                Event = NoteEventType.Updated.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                aggregate: note,
                eventType: NoteEventType.Updated,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult<OutboxMessage, NoteDetailDto>(_ => note.ToDetailDto());
            }

            // 5) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _noteRepository.Update(note);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Note {NoteId} updated for user {UserId}.",
                note.Id,
                userId);

            return Result.Ok(note.ToDetailDto());
        }
    }
}
