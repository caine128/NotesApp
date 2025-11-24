using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetMonthOverviewQueryHandler
    : IRequestHandler<GetMonthOverviewQuery, Result<IReadOnlyList<DayTasksOverviewDto>>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly ILogger<GetMonthOverviewQueryHandler> _logger;

        public GetMonthOverviewQueryHandler(ICurrentUserService currentUserService,
                                            ITaskRepository taskRepository,
                                            ILogger<GetMonthOverviewQueryHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<DayTasksOverviewDto>>> Handle(
            GetMonthOverviewQuery request,
            CancellationToken cancellationToken)
        {
            // Resolve current user (this already goes through our Entra/TestAuth pipeline).
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var firstDayOfMonth = new DateOnly(request.Year, request.Month, 1);
            var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);

            _logger.LogInformation(
                "Getting month overview for user {UserId} for {Year}-{Month}",
                userId, request.Year, request.Month);

            var overview = await _taskRepository.GetOverviewForDateRangeAsync(
                userId,
                firstDayOfMonth,
                firstDayOfNextMonth,
                cancellationToken);

            // No real failure mode here unless repository throws; those are handled by
            // our global exception handler, so we return an Ok result.
            return Result.Ok<IReadOnlyList<DayTasksOverviewDto>>(overview);
        }
    }
}
