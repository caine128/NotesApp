using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    /// <summary>
    /// Returns all notes for the current user on a given date.
    /// 
    /// Flow:
    /// 1. Resolve current user Id from ICurrentUserService.
    /// 2. Ask INoteRepository for all notes for that user+date.
    /// 3. Map domain entities to NoteDto list.
    /// 4. Wrap in Result<IReadOnlyList<NoteDto>>.
    /// </summary>
    public sealed class GetNotesForDayQueryHandler
        : IRequestHandler<GetNotesForDayQuery, Result<IReadOnlyList<NoteDto>>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetNotesForDayQueryHandler> _logger;

        public GetNotesForDayQueryHandler(INoteRepository noteRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetNotesForDayQueryHandler> logger)
        {
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<NoteDto>>> Handle(GetNotesForDayQuery request,
                                                                 CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            _logger.LogInformation("Fetching notes for user {UserId} on date {Date}",
                                   userId,
                                   request.Date);

            var notes = await _noteRepository.GetForDayAsync(userId,
                                                             request.Date,
                                                             cancellationToken);

            var dtoList = notes.ToDtoList();

            _logger.LogInformation("Found {NoteCount} notes for user {UserId} on date {Date}",
                                   dtoList.Count,
                                   userId,
                                   request.Date);

            return Result.Ok<IReadOnlyList<NoteDto>>(dtoList);
        }
    }
}
