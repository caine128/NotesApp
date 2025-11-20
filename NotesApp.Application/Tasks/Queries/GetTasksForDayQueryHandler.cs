using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTasksForDayQueryHandler
                : IRequestHandler<GetTasksForDayQuery, Result<IReadOnlyList<TaskDto>>>
    {
        private readonly ITaskRepository _taskRepository;

        public GetTasksForDayQueryHandler(ITaskRepository taskRepository)
        {
            _taskRepository = taskRepository;
        }

        public async Task<Result<IReadOnlyList<TaskDto>>> Handle(
            GetTasksForDayQuery request,
            CancellationToken cancellationToken)
        {
            var tasks = await _taskRepository.GetForDayAsync(request.UserId,
                                                             request.Date,
                                                             cancellationToken);

            var dtoList = tasks.ToDtoList();

            return Result.Ok<IReadOnlyList<TaskDto>>(dtoList);
        }
    }
}
