using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Tasks.Commands.DeleteTask
{
    /// <summary>
    /// Handles the DeleteTaskCommand:
    /// - Resolves the current internal user id from the token.
    /// - Loads the task WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the task belongs to the current user.
    /// - Soft-deletes the task through the TaskItem domain method.
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    ///
    /// Returns:
    /// - Result.Ok()                 -> HTTP 204 No Content
    /// - Result.Fail (Tasks.NotFound)-> HTTP 404 Not Found
    /// - Other failures              -> HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteTaskCommandHandler
        : IRequestHandler<DeleteTaskCommand, Result>
    {
        private readonly ITaskRepository _taskRepository;
        // REFACTORED: added subtask repository for subtasks cascade-delete
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteTaskCommandHandler> _logger;

        public DeleteTaskCommandHandler(
            ITaskRepository taskRepository,
            ISubtaskRepository subtaskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteTaskCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _subtaskRepository = subtaskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteTaskCommand command, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the task WITHOUT tracking
            //    This ensures the soft-delete modification won't auto-persist if outbox creation fails
            var taskItem = await _taskRepository.GetByIdUntrackedAsync(command.TaskId, cancellationToken);

            if (taskItem is null || taskItem.UserId != currentUserId)
            {
                _logger.LogWarning("DeleteTask failed: task {TaskId} not found for user {UserId}.",
                                   command.TaskId,
                                   currentUserId);

                return Result.Fail(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain soft delete (entity is NOT tracked, so modifications are in-memory only)
            var deleteResult = taskItem.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return deleteResult.ToResult();
            }

            // 4) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                TaskId = taskItem.Id,
                taskItem.UserId,
                taskItem.Date,
                taskItem.Title,
                Event = TaskEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                aggregate: taskItem,
                eventType: TaskEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult();
            }

            // 5) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _taskRepository.Update(taskItem);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);

            // REFACTORED: cascade soft-delete all subtasks atomically (subtasks feature)
            // Bulk-deletes in the same SaveChangesAsync call so deleted subtasks surface
            // in the next sync pull via UpdatedAtUtc.
            await _subtaskRepository.SoftDeleteAllForTaskAsync(taskItem.Id, currentUserId, utcNow, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Task {TaskId} soft-deleted for user {UserId}.",
                                   taskItem.Id,
                                   currentUserId);

            return Result.Ok();
        }
    }
}
