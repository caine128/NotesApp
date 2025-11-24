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
    public sealed class GetYearOverviewQueryHandler
    : IRequestHandler<GetYearOverviewQuery, Result<IReadOnlyList<MonthTasksOverviewDto>>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly ILogger<GetYearOverviewQueryHandler> _logger;

        public GetYearOverviewQueryHandler(ICurrentUserService currentUserService,
                                           ITaskRepository taskRepository,
                                           ILogger<GetYearOverviewQueryHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<MonthTasksOverviewDto>>> Handle(GetYearOverviewQuery request,
                                                                               CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            _logger.LogInformation(
                "Getting year overview for user {UserId} for year {Year}",
                userId,
                request.Year);

            var overview = await _taskRepository.GetYearOverviewAsync(userId,
                                                                      request.Year,
                                                                      cancellationToken);

            return Result.Ok<IReadOnlyList<MonthTasksOverviewDto>>(overview);
        }
    }
}
