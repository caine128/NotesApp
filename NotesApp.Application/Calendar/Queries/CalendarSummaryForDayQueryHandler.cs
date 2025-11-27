using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Calendar.Models;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed class CalendarSummaryForDayQueryHandler
    : IRequestHandler<CalendarSummaryForDayQuery, Result<CalendarSummaryDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;

        public CalendarSummaryForDayQueryHandler(ITaskRepository taskRepository,
                                                 INoteRepository noteRepository,
                                                 ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<CalendarSummaryDto>> Handle(CalendarSummaryForDayQuery request,
                                                             CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var tasks = await _taskRepository.GetForDayAsync(userId,
                                                             request.Date,
                                                             cancellationToken);

            var notes = await _noteRepository.GetForDayAsync(userId,
                                                             request.Date,
                                                             cancellationToken);

            // Reuse the range mapper for [Date, Date+1)
            var dto = CalendarMappings.ToCalendarSummaryDto(
                request.Date,
                tasks,
                notes);


            return Result.Ok(dto);
        }
    }
}
