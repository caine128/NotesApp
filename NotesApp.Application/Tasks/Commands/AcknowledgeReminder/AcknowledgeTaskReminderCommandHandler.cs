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
    /// - Ensures the task exists and belongs to the current user.
    /// - Ensures the task has a reminder set.
    /// - Calls TaskItem.AcknowledgeReminder (which increments Version and timestamps).
    /// - Emits a Task.Updated outbox message so other devices can sync.
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

            var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken);

            if (task is null || task.UserId != userId)
            {
                return Result.Fail("Task not found.");
            }

            if (task.IsDeleted)
            {
                return Result.Fail("Task is deleted.");
            }

            if (!task.ReminderAtUtc.HasValue)
            {
                return Result.Fail("Task does not have a reminder set.");
            }

            var domainResult = task.AcknowledgeReminder(request.AcknowledgedAtUtc, utcNow);

            if (domainResult.IsFailure)
            {
                var messages = domainResult.Errors.Select(e => e.Message).ToArray();
                return Result.Fail(messages);
            }

            // Build a Task.Updated outbox message so other devices can sync the change
            var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);
            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                task,
                TaskEventType.Updated,
                payload,
                utcNow);

            if (outboxResult.IsSuccess)
            {
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }     
    }
}
