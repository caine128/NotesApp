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
    public sealed class GetTaskDetailQueryHandler
    : IRequestHandler<GetTaskDetailQuery, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetTaskDetailQueryHandler(
            ITaskRepository taskRepository,
            ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<TaskDetailDto>> Handle(
            GetTaskDetailQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken);

            if (task is null || task.UserId != userId)
            {
                return Result.Fail(new Error("Task.NotFound"));
                    
            }

            var dto = task.ToDetailDto();
            return Result.Ok(dto);
        }
    }
}
