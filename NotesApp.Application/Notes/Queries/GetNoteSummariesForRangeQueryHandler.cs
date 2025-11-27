using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    public sealed class GetNoteSummariesForRangeQueryHandler
    : IRequestHandler<GetNoteSummariesForRangeQuery, Result<IReadOnlyList<NoteSummaryDto>>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetNoteSummariesForRangeQueryHandler(
            INoteRepository noteRepository,
            ICurrentUserService currentUserService)
        {
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<NoteSummaryDto>>> Handle(
            GetNoteSummariesForRangeQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var notes = await _noteRepository.GetForDateRangeAsync(
                userId,
                request.Start,
                request.EndExclusive,
                cancellationToken);

            var summaries = notes
                .OrderBy(n => n.Date)
                .ToSummaryDtoList();

            return Result.Ok<IReadOnlyList<NoteSummaryDto>>(summaries);
        }
    }
}
