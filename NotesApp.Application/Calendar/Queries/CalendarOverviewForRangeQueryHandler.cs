using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Calendar.Models;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Notes;
using NotesApp.Application.Tasks;
using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed class CalendarOverviewForRangeQueryHandler
    : IRequestHandler<CalendarOverviewForRangeQuery, Result<IReadOnlyList<CalendarOverviewDto>>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public CalendarOverviewForRangeQueryHandler(
            ITaskRepository taskRepository,
            INoteRepository noteRepository,
            ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<CalendarOverviewDto>>> Handle(
            CalendarOverviewForRangeQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var tasks = await _taskRepository.GetForDateRangeAsync(
                userId,
                request.Start,
                request.EndExclusive,
                cancellationToken);

            var notes = await _noteRepository.GetForDateRangeAsync(
                userId,
                request.Start,
                request.EndExclusive,
                cancellationToken);

            var overview = CalendarMappings.ToCalendarOverviewDtoList(
                request.Start,
                request.EndExclusive,
                tasks,
                notes);

            return Result.Ok<IReadOnlyList<CalendarOverviewDto>>(overview);
        }
    }
}
