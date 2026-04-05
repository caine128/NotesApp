using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Text.Json;

namespace NotesApp.Application.Subtasks.Commands.DeleteSubtask
{
    /// <summary>
    /// Handles <see cref="DeleteSubtaskCommand"/>:
    /// - Loads the subtask WITHOUT tracking to prevent auto-persistence on failure.
    /// - Validates the subtask belongs to both the current user and the specified task.
    /// - Soft-deletes through the domain method.
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes via UnitOfWork.
    ///
    /// Returns:
    /// - Result.Ok() → HTTP 204 No Content
    /// - Result.Fail (Subtasks.NotFound) → HTTP 404 Not Found
    /// - Other failures → HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteSubtaskCommandHandler
        : IRequestHandler<DeleteSubtaskCommand, Result>
    {
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteSubtaskCommandHandler> _logger;

        public DeleteSubtaskCommandHandler(
            ISubtaskRepository subtaskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteSubtaskCommandHandler> logger)
        {
            _subtaskRepository = subtaskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteSubtaskCommand command,
                                          CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // Load WITHOUT tracking so soft-delete won't auto-persist if outbox creation fails.
            var subtask = await _subtaskRepository.GetByIdUntrackedAsync(command.SubtaskId, cancellationToken);

            if (subtask is null || subtask.UserId != userId || subtask.TaskId != command.TaskId)
            {
                _logger.LogWarning(
                    "DeleteSubtask failed: Subtask {SubtaskId} not found for user {UserId} / task {TaskId}",
                    command.SubtaskId,
                    userId,
                    command.TaskId);

                return Result.Fail(
                    new Error("Subtask not found.")
                        .WithMetadata("ErrorCode", "Subtasks.NotFound"));
            }

            // Domain soft delete (entity is NOT tracked, so modification is in-memory only).
            var deleteResult = subtask.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                return deleteResult.ToResult();
            }

            // Create outbox message BEFORE persisting.
            var payload = JsonSerializer.Serialize(new
            {
                SubtaskId = subtask.Id,
                subtask.UserId,
                subtask.TaskId,
                Event = SubtaskEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                aggregate: subtask,
                eventType: SubtaskEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                return outboxResult.ToResult();
            }

            // SUCCESS: attach untracked entity and persist.
            _subtaskRepository.Update(subtask);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Subtask {SubtaskId} soft-deleted for user {UserId}",
                subtask.Id,
                userId);

            return Result.Ok();
        }
    }
}
