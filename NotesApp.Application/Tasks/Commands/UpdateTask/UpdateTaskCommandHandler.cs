using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateTaskCommandHandler> _logger;

        public UpdateTaskCommandHandler(
            ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<UpdateTaskCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<TaskDetailDto>> Handle(UpdateTaskCommand command, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id (our account-linking pattern)
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the task WITHOUT tracking
            //    This ensures modifications won't auto-persist if we return early due to failures
            var taskItem = await _taskRepository.GetByIdUntrackedAsync(command.TaskId, cancellationToken);

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

            // 3) Domain update (title + date + new fields) (entity is NOT tracked, so modifications are in-memory only)
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
                // Entity modified but NOT tracked - won't persist
                return updateResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 4) Domain reminder update
            var reminderResult = taskItem.SetReminder(command.ReminderAtUtc, utcNow);
            if (reminderResult.IsFailure)
            {
                return reminderResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 5) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                TaskId = taskItem.Id,
                taskItem.UserId,
                taskItem.Date,
                taskItem.Title,
                taskItem.Description,
                taskItem.StartTime,
                taskItem.EndTime,
                taskItem.Location,
                taskItem.TravelTime,
                taskItem.IsCompleted,
                taskItem.ReminderAtUtc,
                Event = TaskEventType.Updated.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(aggregate: taskItem,
                                                                             eventType: TaskEventType.Updated,
                                                                             payload: payload,
                                                                             utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => taskItem.ToDetailDto());
            }

            // 6) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _taskRepository.Update(taskItem);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Task {TaskId} updated for user {UserId}.",
                                   taskItem.Id,
                                   currentUserId);

            return Result.Ok(taskItem.ToDetailDto());
        }
    }
}
