using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.UpdateTask
{
    /// <summary>
    /// Handles the UpdateTaskCommand:
    /// - Ensures the task belongs to the current user
    /// - Applies domain logic (update + reminder)
    /// - Persists changes via ITaskRepository + IUnitOfWork
    /// - Returns a Result&lt;TaskDto&gt; for consistent error handling.
    /// </summary>
    public sealed class UpdateTaskCommandHandler 
        : IRequestHandler<UpdateTaskCommand, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateTaskCommandHandler> _logger;

        public UpdateTaskCommandHandler(
            ITaskRepository taskRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<UpdateTaskCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<TaskDetailDto>> Handle(UpdateTaskCommand command, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id (our account-linking pattern)
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the task from repository
            var taskItem = await _taskRepository.GetByIdAsync(command.TaskId, cancellationToken);

            if (taskItem is null || taskItem.UserId != currentUserId)
            {
                // Do not leak information about existence vs authorization.
                _logger.LogWarning("UpdateTask failed: task {TaskId} not found for user {UserId}.",
                                   command.TaskId,
                                   currentUserId);
                return Result.Fail<TaskDetailDto>(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain update (title + date + new fields)
            var updateResult = taskItem.Update(title: command.Title,
                                               date: command.Date,
                                               description: command.Description,
                                               startTime: command.StartTime,
                                               endTime: command.EndTime,
                                               location: command.Location,
                                               travelTime: command.TravelTime,
                                               utcNow: utcNow);
            if (updateResult.IsFailure)
            {
                // Convert DomainResult -> Result<TaskDto> with current DTO as value
                return updateResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 4) Domain reminder update
            var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);
            if (reminderResult.IsFailure)
            {
                return reminderResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 5) Persist changes
            // NEW: mark entity as modified
            _taskRepository.Update(taskItem);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 6) Map to DTO
            var dto = taskItem.ToDetailDto();
            _logger.LogInformation("Task {TaskId} updated for user {UserId}.",
                                   taskItem.Id,
                                   currentUserId);

            return Result.Ok(dto);
        }
    }
}
