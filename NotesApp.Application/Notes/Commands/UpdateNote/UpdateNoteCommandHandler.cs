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

        public async Task<Result<NoteDetailDto>> Handle(
            UpdateNoteCommand command,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var note = await _noteRepository.GetByIdAsync(command.NoteId, cancellationToken);

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

            var utcNow = _clock.UtcNow;

            var updateResult = note.Update(
                title: command.Title,
                content: command.Content,
                summary: command.Summary,
                tags: command.Tags,
                date: command.Date,
                utcNow: utcNow);

            if (updateResult.IsFailure)
            {
                return updateResult.ToResult(() => note.ToDetailDto());
            }

            _noteRepository.Update(note);

            // Outbox for NoteUpdated
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
                return outboxResult.ToResult<OutboxMessage, NoteDetailDto>(_ => note.ToDetailDto());
            }

            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Note {NoteId} updated for user {UserId}.",
                note.Id,
                userId);

            var dto = note.ToDetailDto();
            return Result.Ok(dto);
        }
    }
}
