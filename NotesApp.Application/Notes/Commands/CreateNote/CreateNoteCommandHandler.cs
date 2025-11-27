using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.CreateNote
{
    /// <summary>
    /// Handles creation of a new note for the current user.
    /// 
    /// This handler:
    /// 1. Resolves the internal user Id from the current identity.
    /// 2. Calls the Note.Create factory to enforce domain invariants.
    /// 3. Persists the new Note via the repository + UnitOfWork.
    /// 4. Returns a NoteDto wrapped in a FluentResults.Result.
    /// </summary>
    public sealed class CreateNoteCommandHandler
        : IRequestHandler<CreateNoteCommand, Result<NoteDetailDto>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<CreateNoteCommandHandler> _logger;

        public CreateNoteCommandHandler(INoteRepository noteRepository,
                                        IUnitOfWork unitOfWork,
                                        ICurrentUserService currentUserService,
                                        ISystemClock clock,
                                        ILogger<CreateNoteCommandHandler> logger)
        {
            _noteRepository = noteRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<NoteDetailDto>> Handle(
            CreateNoteCommand command,
            CancellationToken cancellationToken)
        {
            // 1) Resolve current internal user Id from token/claims.
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Get the current UTC time via clock abstraction.
            var utcNow = _clock.UtcNow;

            // 3) Domain: call the factory which enforces invariants.
            var createResult = Note.Create(userId: userId,
                                           date: command.Date,
                                           title: command.Title,
                                           content: command.Content,
                                           summary: command.Summary,
                                           tags: command.Tags,
                                           utcNow: utcNow);

            if (createResult.IsFailure)
            {
                // Convert DomainResult<Note> -> Result<NoteDto>
                return createResult.ToResult<Note, NoteDetailDto>(note => note.ToDetailDto());
            }

            var note = createResult.Value;

            // 4) Persistence: repository + unit of work.
            await _noteRepository.AddAsync(note, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created note {NoteId} for user {UserId} on {Date}",
                                   note.Id,
                                   note.UserId,
                                   note.Date);

            // 5) Map domain entity -> DTO
            var dto = note.ToDetailDto();
            return Result.Ok(dto);
        }
    }
}
