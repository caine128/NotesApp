using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTaskSummariesForRangeQueryHandler
    : IRequestHandler<GetTaskSummariesForRangeQuery, Result<IReadOnlyList<TaskSummaryDto>>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetTaskSummariesForRangeQueryHandler(
            ITaskRepository taskRepository,
            ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<TaskSummaryDto>>> Handle(
            GetTaskSummariesForRangeQuery request,
            CancellationToken cancellationToken)
        {
            // Throws if user is not authenticated; handled by global middleware
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var tasks = await _taskRepository.GetForDateRangeAsync(userId,
                                                                   request.Start,
                                                                   request.EndExclusive,
                                                                   cancellationToken);

            var summaries = tasks
                 .OrderBy(t => t.Date)
                 .ThenBy(t => t.StartTime)
                 .ToSummaryDtoList();

            return Result.Ok<IReadOnlyList<TaskSummaryDto>>(summaries);
        }
    }
}
