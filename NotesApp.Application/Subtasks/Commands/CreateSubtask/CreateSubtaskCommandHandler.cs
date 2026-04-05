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

namespace NotesApp.Application.Subtasks.Commands.CreateSubtask
{
    /// <summary>
    /// Handles <see cref="CreateSubtaskCommand"/>:
    /// - Validates the parent task exists and belongs to the current user.
    /// - Creates the subtask via the domain factory.
    /// - Emits an outbox message for the Created event.
    /// - Persists changes via UnitOfWork.
    ///
    /// Returns:
    /// - Result.Ok(SubtaskDto) → HTTP 201 Created
    /// - Result.Fail (Subtasks.ParentNotFound) → HTTP 404 Not Found
    /// - Other failures → HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class CreateSubtaskCommandHandler
        : IRequestHandler<CreateSubtaskCommand, Result<SubtaskDto>>
    {
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<CreateSubtaskCommandHandler> _logger;

        public CreateSubtaskCommandHandler(
            ISubtaskRepository subtaskRepository,
            ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<CreateSubtaskCommandHandler> logger)
        {
            _subtaskRepository = subtaskRepository;
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<SubtaskDto>> Handle(CreateSubtaskCommand command,
                                                      CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 1) Validate parent task
            var task = await _taskRepository.GetByIdAsync(command.TaskId, cancellationToken);

            if (task is null || task.UserId != userId || task.IsDeleted)
            {
                _logger.LogWarning(
                    "CreateSubtask failed: Task {TaskId} not found for user {UserId}",
                    command.TaskId,
                    userId);

                return Result.Fail<SubtaskDto>(
                    new Error("Parent task not found.")
                        .WithMetadata("ErrorCode", "Subtasks.ParentNotFound"));
            }

            // 2) Create via domain factory
            var createResult = Subtask.Create(userId, command.TaskId, command.Text, command.Position, utcNow);

            if (createResult.IsFailure)
            {
                return createResult.ToResult<Subtask, SubtaskDto>(s => s.ToSubtaskDto());
            }

            var subtask = createResult.Value!;

            // 3) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                SubtaskId = subtask.Id,
                subtask.UserId,
                subtask.TaskId,
                subtask.Text,
                subtask.Version,
                Event = SubtaskEventType.Created.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                aggregate: subtask,
                eventType: SubtaskEventType.Created,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                return outboxResult.ToResult<OutboxMessage, SubtaskDto>(_ => subtask.ToSubtaskDto());
            }

            // 4) Persist
            await _subtaskRepository.AddAsync(subtask, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Subtask {SubtaskId} created for task {TaskId} by user {UserId}",
                subtask.Id,
                command.TaskId,
                userId);

            return Result.Ok(subtask.ToSubtaskDto());
        }
    }
}
