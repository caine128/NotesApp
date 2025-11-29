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
    /// 1) Resolves the current user from the token.
    /// 2) Loads the TaskItem from the repository.
    /// 3) Ensures the task belongs to the current user.
    /// 4) Applies the completion state in the domain (MarkCompleted/MarkPending).
    /// 5) Persists the change using UnitOfWork.
    /// 6) Returns the updated TaskDto wrapped in a FluentResults.Result.
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

            // 2) Load the task from the repository
            var taskItem = await _taskRepository
                .GetByIdAsync(command.TaskId, cancellationToken);

            if (taskItem is null || taskItem.UserId != currentUserId)
            {
                // We deliberately return "not found" instead of "forbidden"
                // to avoid leaking existence of other users' tasks.
                _logger.LogWarning(
                    "SetTaskCompletion failed: task {TaskId} not found for user {UserId}.",
                    command.TaskId,
                    currentUserId);

                return Result.Fail<TaskDetailDto>(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Apply the desired completion state at the domain level.
            // These methods are already idempotent and enforce invariants.
            var completionResult = command.IsCompleted
                ? taskItem.MarkCompleted(utcNow)
                : taskItem.MarkPending(utcNow);

            if (completionResult.IsFailure)
            {
                // Convert DomainResult -> Result<TaskDto> with the current DTO as value.
                // This preserves any domain error codes/messages while still returning
                // the current state of the task to the client.
                return completionResult.ToResult(() => taskItem.ToDetailDto());
            }

            // 4) Mark the entity as modified so EF Core tracks the change.
            _taskRepository.Update(taskItem);

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

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                aggregate: taskItem,
                eventType: TaskEventType.CompletionChanged,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult<OutboxMessage, TaskDetailDto>(_ => taskItem.ToDetailDto());
            }

            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 6) Return updated DTO as Result.Ok<T>
            var dto = taskItem.ToDetailDto();
            return Result.Ok(dto);
        }
    }
}
