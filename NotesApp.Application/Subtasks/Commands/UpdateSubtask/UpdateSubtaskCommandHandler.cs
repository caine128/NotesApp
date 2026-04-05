using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Text.Json;

namespace NotesApp.Application.Subtasks.Commands.UpdateSubtask
{
    /// <summary>
    /// Handles <see cref="UpdateSubtaskCommand"/>:
    /// - Loads the subtask WITHOUT tracking to prevent auto-persistence on failure.
    /// - Validates the subtask belongs to both the current user and the specified task.
    /// - Applies only the fields that are non-null (null = no change).
    /// - Creates outbox message BEFORE persisting (skipped when there are no changes).
    /// - Persists changes via UnitOfWork.
    ///
    /// Returns:
    /// - Result.Ok(SubtaskDto) → HTTP 200 OK
    /// - Result.Fail (Subtasks.NotFound) → HTTP 404 Not Found
    /// - Other failures → HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class UpdateSubtaskCommandHandler
        : IRequestHandler<UpdateSubtaskCommand, Result<SubtaskDto>>
    {
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateSubtaskCommandHandler> _logger;

        public UpdateSubtaskCommandHandler(
            ISubtaskRepository subtaskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<UpdateSubtaskCommandHandler> logger)
        {
            _subtaskRepository = subtaskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<SubtaskDto>> Handle(UpdateSubtaskCommand command,
                                                      CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // Load WITHOUT tracking so modifications won't auto-persist if we return early.
            var subtask = await _subtaskRepository.GetByIdUntrackedAsync(command.SubtaskId, cancellationToken);

            if (subtask is null || subtask.UserId != userId || subtask.TaskId != command.TaskId)
            {
                _logger.LogWarning(
                    "UpdateSubtask failed: Subtask {SubtaskId} not found for user {UserId} / task {TaskId}",
                    command.SubtaskId,
                    userId,
                    command.TaskId);

                return Result.Fail<SubtaskDto>(
                    new Error("Subtask not found.")
                        .WithMetadata("ErrorCode", "Subtasks.NotFound"));
            }

            if (subtask.IsDeleted)
            {
                return Result.Fail<SubtaskDto>(
                    new Error("Cannot update a deleted subtask.")
                        .WithMetadata("ErrorCode", "Subtasks.Deleted"));
            }

            // Apply only the fields that are non-null (null = no change).
            var hasChanges = false;

            if (command.Text is not null)
            {
                var textResult = subtask.UpdateText(command.Text, utcNow);
                if (textResult.IsFailure)
                {
                    return textResult.ToResult(() => subtask.ToSubtaskDto());
                }
                hasChanges = true;
            }

            if (command.IsCompleted.HasValue)
            {
                var completedResult = subtask.SetCompleted(command.IsCompleted.Value, utcNow);
                if (completedResult.IsFailure)
                {
                    return completedResult.ToResult(() => subtask.ToSubtaskDto());
                }
                hasChanges = true;
            }

            if (command.Position is not null)
            {
                var posResult = subtask.UpdatePosition(command.Position, utcNow);
                if (posResult.IsFailure)
                {
                    return posResult.ToResult(() => subtask.ToSubtaskDto());
                }
                hasChanges = true;
            }

            // If no fields were changed, return the current state without a DB write.
            if (!hasChanges)
            {
                return Result.Ok(subtask.ToSubtaskDto());
            }

            // Create outbox message BEFORE persisting.
            var payload = JsonSerializer.Serialize(new
            {
                SubtaskId = subtask.Id,
                subtask.UserId,
                subtask.TaskId,
                subtask.Text,
                subtask.Version,
                Event = SubtaskEventType.Updated.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                aggregate: subtask,
                eventType: SubtaskEventType.Updated,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                return outboxResult.ToResult<OutboxMessage, SubtaskDto>(_ => subtask.ToSubtaskDto());
            }

            // SUCCESS: attach untracked entity and persist.
            _subtaskRepository.Update(subtask);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Subtask {SubtaskId} updated for user {UserId}",
                subtask.Id,
                userId);

            return Result.Ok(subtask.ToSubtaskDto());
        }
    }
}
