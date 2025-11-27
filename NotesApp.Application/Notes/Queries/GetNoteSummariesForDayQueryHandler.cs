using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
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
    public sealed class GetNoteSummariesForDayQueryHandler
        : IRequestHandler<GetNoteSummariesForDayQuery, Result<IReadOnlyList<NoteSummaryDto>>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetNoteSummariesForDayQueryHandler> _logger;

        public GetNoteSummariesForDayQueryHandler(INoteRepository noteRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetNoteSummariesForDayQueryHandler> logger)
        {
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<NoteSummaryDto>>> Handle(GetNoteSummariesForDayQuery request,
                                                                 CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            _logger.LogInformation("Fetching notes for user {UserId} on date {Date}",
                                   userId,
                                   request.Date);

            var notes = await _noteRepository.GetForDayAsync(userId,
                                                             request.Date,
                                                             cancellationToken);

            var dtoList = notes
                    .OrderBy(n => n.Date)   // mostly same day, but fine
                    .ToSummaryDtoList();

            _logger.LogInformation("Found {NoteCount} notes for user {UserId} on date {Date}",
                                   dtoList.Count,
                                   userId,
                                   request.Date);

            return Result.Ok<IReadOnlyList<NoteSummaryDto>>(dtoList);
        }
    }
}
