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

namespace NotesApp.Application.Tasks.Commands.SetTaskCompletion
{
    /// <summary>
    /// Handles the SetTaskCompletionCommand:
    /// - Loads the task WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the task belongs to the current user and is not deleted.
    /// - Applies the completion state in the domain (MarkCompleted/MarkPending).
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    /// </summary>
    public sealed class SetTaskCompletionCommandHandler
        : IRequestHandler<SetTaskCompletionCommand, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<SetTaskCompletionCommandHandler> _logger;

        public SetTaskCompletionCommandHandler(ITaskRepository taskRepository,
                                               IOutboxRepository outboxRepository,
                                               IUnitOfWork unitOfWork,
                                               ICurrentUserService currentUserService,
                                               ISystemClock clock,
                                               ILogger<SetTaskCompletionCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<TaskDetailDto>> Handle(SetTaskCompletionCommand command,
                                                  CancellationToken cancellationToken)
        {
            // 1) Resolve current internal user Id from token/claims.
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the task WITHOUT tracking
            //    This ensures modifications won't auto-persist if we return early due to failures
            var taskItem = await _taskRepository
                .GetByIdUntrackedAsync(command.TaskId, cancellationToken);

            if (taskItem is null || taskItem.UserId != currentUserId)
            {
                _logger.LogWarning(
                    "SetTaskCompletion failed: task {TaskId} not found for user {UserId}.",
                    command.TaskId,
                    currentUserId);

                return Result.Fail<TaskDetailDto>(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            if (taskItem.IsDeleted)
            {
                return Result.Fail<TaskDetailDto>(
                    new Error("Cannot modify a deleted task.")
                        .WithMetadata("ErrorCode", "Tasks.Deleted"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Apply the desired completion state (entity is NOT tracked, modifications are in-memory only)
            var completionResult = command.IsCompleted
                ? taskItem.MarkCompleted(utcNow)
                : taskItem.MarkPending(utcNow);

            if (completionResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return completionResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 4) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                TaskId = taskItem.Id,
                taskItem.UserId,
                taskItem.Date,
                taskItem.Title,
                taskItem.IsCompleted,
                Event = TaskEventType.CompletionChanged.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(aggregate: taskItem,
                                                                             eventType: TaskEventType.CompletionChanged,
                                                                             payload: payload,
                                                                             utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => taskItem.ToDetailDto());
            }

            // 5) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _taskRepository.Update(taskItem);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Task {TaskId} completion set to {IsCompleted} for user {UserId}.",
                taskItem.Id,
                taskItem.IsCompleted,
                currentUserId);

            return Result.Ok(taskItem.ToDetailDto());
        }
    }
}
