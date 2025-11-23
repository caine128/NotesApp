using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.DeleteTask
{
    /// <summary>
    /// Handles the DeleteTaskCommand:
    /// - Resolves the current internal user id from the token.
    /// - Loads the task from repository.
    /// - Ensures the task belongs to the current user.
    /// - Soft-deletes the task through the TaskItem domain method.
    /// - Persists changes via UnitOfWork.
    /// 
    /// Returns:
    /// - Result.Ok()                 -> HTTP 204 No Content
    /// - Result.Fail (Tasks.NotFound)-> HTTP 404 Not Found (via our profile)
    /// - Other failures              -> HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteTaskCommandHandler
        : IRequestHandler<DeleteTaskCommand, Result>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteTaskCommandHandler> _logger;

        public DeleteTaskCommandHandler(
            ITaskRepository taskRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteTaskCommandHandler> logger)
        {
            _taskRepository = taskRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteTaskCommand command, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id (account-linking pattern).
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the task from repository.
            var taskItem = await _taskRepository.GetByIdAsync(command.TaskId, cancellationToken);

            if (taskItem is null || taskItem.UserId != currentUserId)
            {
                // Do not leak info about existence vs authorization.
                _logger.LogWarning("DeleteTask failed: task {TaskId} not found for user {UserId}.",
                                   command.TaskId,
                                   currentUserId);

                return Result.Fail(
                    new Error("Task not found.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain soft delete.
            //    This uses your existing TaskItem.SoftDelete(utcNow) method and keeps
            //    all invariants and audit fields (UpdatedAtUtc, IsDeleted).
            var deleteResult = taskItem.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Map DomainResult -> Result (no value).
                return deleteResult.ToResult();
            }

            // 4) Persist changes.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Task {TaskId} soft-deleted for user {UserId}.",
                                   taskItem.Id,
                                   currentUserId);

            // No payload needed for delete -> Result.Ok() => 204 No Content via our profile.
            return Result.Ok();
        }
    }
}
