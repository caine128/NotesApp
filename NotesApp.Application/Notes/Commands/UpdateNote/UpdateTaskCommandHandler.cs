using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.UpdateNote
{
    public sealed class UpdateNoteCommandHandler
    : IRequestHandler<UpdateNoteCommand, Result<NoteDetailDto>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateNoteCommandHandler> _logger;

        public UpdateNoteCommandHandler(INoteRepository noteRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock,
                                        ILogger<UpdateNoteCommandHandler> logger)
        {
            _noteRepository = noteRepository;
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
