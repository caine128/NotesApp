using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Calendar.Models;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed class CalendarSummaryForRangeQueryHandler
    : IRequestHandler<CalendarSummaryForRangeQuery, Result<IReadOnlyList<CalendarSummaryDto>>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public CalendarSummaryForRangeQueryHandler(ITaskRepository taskRepository,
                                                   INoteRepository noteRepository,
                                                   ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<CalendarSummaryDto>>> Handle(CalendarSummaryForRangeQuery request,
                                                                            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var tasks = await _taskRepository.GetForDateRangeAsync(userId,
                                                                   request.Start,
                                                                   request.EndExclusive,
                                                                   cancellationToken);

            var notes = await _noteRepository.GetForDateRangeAsync(userId,
                                                                   request.Start,
                                                                   request.EndExclusive,
                                                                   cancellationToken);

            var summaries = CalendarMappings.ToCalendarSummaryDtoList(
                request.Start,
                request.EndExclusive,
                tasks,
                notes);

            return Result.Ok<IReadOnlyList<CalendarSummaryDto>>(summaries);
        }
    }
}
