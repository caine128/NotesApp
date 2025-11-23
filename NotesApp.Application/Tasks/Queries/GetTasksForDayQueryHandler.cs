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
    public sealed class GetTasksForDayQueryHandler
                : IRequestHandler<GetTasksForDayQuery, Result<IReadOnlyList<TaskDto>>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetTasksForDayQueryHandler> _logger;

        public GetTasksForDayQueryHandler(ITaskRepository taskRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetTasksForDayQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<TaskDto>>> Handle(GetTasksForDayQuery request,
                                                                 CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var tasks = await _taskRepository.GetForDayAsync(userId,
                                                             request.Date,
                                                             cancellationToken);

            var dtoList = tasks.ToDtoList();

            return Result.Ok<IReadOnlyList<TaskDto>>(dtoList);
        }
    }
}
