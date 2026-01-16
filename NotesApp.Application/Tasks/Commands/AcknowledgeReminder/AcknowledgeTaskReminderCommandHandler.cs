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

namespace NotesApp.Application.Tasks.Commands.AcknowledgeReminder
{
    /// <summary>
    /// Handles acknowledging a reminder for a task.
    /// 
    /// - Loads the task WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the task exists and belongs to the current user.
    /// - Ensures the task has a reminder set and is not deleted.
    /// - Calls TaskItem.AcknowledgeReminder (which increments Version and timestamps).
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    /// </summary>
    public sealed class AcknowledgeTaskReminderCommandHandler
        : IRequestHandler<AcknowledgeTaskReminderCommand, Result>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<AcknowledgeTaskReminderCommandHandler> _logger;

        public AcknowledgeTaskReminderCommandHandler(
            ICurrentUserService currentUserService,
            ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            ILogger<AcknowledgeTaskReminderCommandHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(AcknowledgeTaskReminderCommand request,
                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation(
                "Acknowledging reminder for task {TaskId} for user {UserId} from device {DeviceId} at {UtcNow}",
                request.TaskId,
                userId,
                request.DeviceId,
                utcNow);

            // 1) Load the task WITHOUT tracking
            //    This ensures modifications won't auto-persist if we return early due to failures
            var task = await _taskRepository.GetByIdUntrackedAsync(request.TaskId, cancellationToken);

            if (task is null || task.UserId != userId)
            {
                return Result.Fail(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            if (task.IsDeleted)
            {
                return Result.Fail(
                    new Error("Task is deleted.")
                        .WithMetadata("ErrorCode", "Tasks.Deleted"));
            }

            if (!task.ReminderAtUtc.HasValue)
            {
                return Result.Fail(
                    new Error("Task does not have a reminder set.")
                        .WithMetadata("ErrorCode", "Tasks.NoReminder"));
            }

            // 2) Apply domain operation (entity is NOT tracked, modifications are in-memory only)
            var domainResult = task.AcknowledgeReminder(request.AcknowledgedAtUtc, utcNow);

            if (domainResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                var messages = domainResult.Errors.Select(e => e.Message).ToArray();
                return Result.Fail(messages);
            }

            // 3) Create outbox message BEFORE persisting
            //    Sync is essential - other devices must know the reminder was acknowledged
            var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);
            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                task,
                TaskEventType.Updated,
                payload,
                utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                // Entity modified but NOT tracked - won't persist
                _logger.LogError(
                    "Failed to create outbox message for task reminder acknowledgment. TaskId: {TaskId}",
                    request.TaskId);
                return Result.Fail(
                    new Error("Failed to create sync event.")
                        .WithMetadata("ErrorCode", "Outbox.CreateFailed"));
            }

            // 4) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _taskRepository.Update(task);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Task {TaskId} reminder acknowledged for user {UserId} from device {DeviceId}",
                request.TaskId,
                userId,
                request.DeviceId);

            return Result.Ok();
        }
    }
}
