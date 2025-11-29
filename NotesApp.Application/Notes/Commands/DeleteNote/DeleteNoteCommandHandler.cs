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
    public sealed class DeleteNoteCommandHandler
    : IRequestHandler<DeleteNoteCommand, Result>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteNoteCommandHandler> _logger;

        public DeleteNoteCommandHandler(INoteRepository noteRepository,
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
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var note = await _noteRepository.GetByIdAsync(request.NoteId, cancellationToken);

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

            var deleteResult = note.SoftDelete(utcNow);
            if (deleteResult.IsFailure)
            {
                return deleteResult.ToResult();
            }

            _noteRepository.Update(note);


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

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult();
            }

            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Note {NoteId} soft-deleted for user {UserId}.",
                note.Id,
                userId);

            return Result.Ok();
        }
    }
}
