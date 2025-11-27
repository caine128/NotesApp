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
    public sealed class GetNoteOverviewForRangeQueryHandler
    : IRequestHandler<GetNoteOverviewForRangeQuery, Result<IReadOnlyList<NoteOverviewDto>>>
    {
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetNoteOverviewForRangeQueryHandler(
            INoteRepository noteRepository,
            ICurrentUserService currentUserService)
        {
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<NoteOverviewDto>>> Handle(
            GetNoteOverviewForRangeQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var notes = await _noteRepository.GetForDateRangeAsync(
                userId,
                request.Start,
                request.EndExclusive,
                cancellationToken);

            var overview = notes
                .OrderBy(n => n.Date)
                .ToOverviewDtoList();

            return Result.Ok<IReadOnlyList<NoteOverviewDto>>(overview);
        }
    }
}
