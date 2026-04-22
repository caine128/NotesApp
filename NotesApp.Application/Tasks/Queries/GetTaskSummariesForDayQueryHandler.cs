using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    /// <summary>
    /// Query handler that returns all tasks for the current authenticated user
    /// on a given calendar date.
    ///
    /// - Resolves the current user from ICurrentUserService (JWT/claims).
    /// - Delegates persistence to ITaskRepository.
    /// - Maps domain entities to TaskDto via mapping extensions.
    /// </summary>
    public sealed class GetTaskSummariesForDayQueryHandler
                : IRequestHandler<GetTaskSummariesForDayQuery, Result<IReadOnlyList<TaskSummaryDto>>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetTaskSummariesForDayQueryHandler> _logger;

        public GetTaskSummariesForDayQueryHandler(ITaskRepository taskRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetTaskSummariesForDayQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<TaskSummaryDto>>> Handle(GetTaskSummariesForDayQuery request,
                                                                 CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            _logger.LogInformation("Fetching tasks for user {UserId} on date {Date}",
                                   userId,
                                   request.Date);

            // REFACTORED: use GetOccurrencesForDayAsync which returns both materialized TaskItems
            // and projected virtual recurring occurrences merged into TaskOccurrenceResult.
            var occurrences = await _taskRepository.GetOccurrencesForDayAsync(userId,
                                                                               request.Date,
                                                                               cancellationToken);

            // Use the list helper — already sorted by StartTime inside GetOccurrencesForDayAsync,
            // but re-sort here for resilience.
            var dtoList = occurrences
                .OrderBy(t => t.StartTime)
                .ToSummaryDtoList();

            _logger.LogInformation("Found {TaskCount} tasks for user {UserId} on date {Date}",
                                   dtoList.Count,
                                   userId,
                                   request.Date);

            return Result.Ok<IReadOnlyList<TaskSummaryDto>>(dtoList);
        }
    }
}
