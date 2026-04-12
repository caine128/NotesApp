using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Attachments;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Subtasks;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed class GetTaskDetailQueryHandler
    : IRequestHandler<GetTaskDetailQuery, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        // REFACTORED: added subtask repository for subtasks feature
        private readonly ISubtaskRepository _subtaskRepository;
        // REFACTORED: added attachment repository for task-attachments feature
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetTaskDetailQueryHandler(
            ITaskRepository taskRepository,
            ISubtaskRepository subtaskRepository,
            IAttachmentRepository attachmentRepository,
            ICurrentUserService currentUserService)
        {
            _taskRepository = taskRepository;
            _subtaskRepository = subtaskRepository;
            _attachmentRepository = attachmentRepository;
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
                return Result.Fail(new Error("Task.NotFound")
                         .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            // REFACTORED: load subtasks separately and include in DTO (subtasks feature)
            // GetAllForTaskAsync returns subtasks ordered by Position (fractional index).
            var subtasks = await _subtaskRepository.GetAllForTaskAsync(task.Id, userId, cancellationToken);
            var subtaskDtos = subtasks.Select(s => s.ToSubtaskDto()).ToList();

            // REFACTORED: load attachments separately and include in DTO (task-attachments feature)
            // GetAllForTaskAsync returns attachments ordered by DisplayOrder (upload order).
            var attachments = await _attachmentRepository.GetAllForTaskAsync(task.Id, userId, cancellationToken);
            var attachmentDtos = attachments.Select(a => a.ToAttachmentDto()).ToList();

            var dto = task.ToDetailDto() with { Subtasks = subtaskDtos, Attachments = attachmentDtos };
            return Result.Ok(dto);
        }
    }
}
