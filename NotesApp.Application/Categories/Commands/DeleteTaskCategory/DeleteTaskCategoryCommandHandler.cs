using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System.Text.Json;

namespace NotesApp.Application.Categories.Commands.DeleteTaskCategory
{
    /// <summary>
    /// Handles the DeleteTaskCategoryCommand (REST/web path only):
    /// - Loads the category WITHOUT tracking.
    /// - Validates ownership.
    /// - If already soft-deleted (category comes back as null due to global query filter),
    ///   still calls ClearCategoryFromTasksAsync to clean up any residual task references,
    ///   then returns success. This is the safe retry path.
    /// - Soft-deletes the category through the domain method.
    /// - Creates an outbox message.
    /// - Clears CategoryId on all affected tasks via the change-tracker pattern, bumping their
    ///   Version so stale mobile push attempts receive a VersionMismatch and emitting a
    ///   Task.Updated SyncChange row per task so the sync feed reflects the cleared FK.
    /// - Commits the category soft-delete + outbox + per-task mutations + SyncChange rows
    ///   atomically in a single SaveChangesAsync.
    ///
    /// Returns:
    /// - Result.Ok()                         -> HTTP 204 No Content
    /// - Result.Fail (Categories.NotFound)   -> HTTP 404 Not Found
    /// - Other failures                       -> HTTP 400 / 500 via global mapping
    /// </summary>
    public sealed class DeleteTaskCategoryCommandHandler
        : IRequestHandler<DeleteTaskCategoryCommand, Result>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly ISyncChangeWriter _syncChangeWriter; // REFACTORED: sequence-based sync pull
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteTaskCategoryCommandHandler> _logger;

        public DeleteTaskCategoryCommandHandler(ICategoryRepository categoryRepository,
                                                ITaskRepository taskRepository,
                                                IOutboxRepository outboxRepository,
                                                ISyncChangeWriter syncChangeWriter,
                                                IUnitOfWork unitOfWork,
                                                ICurrentUserService currentUserService,
                                                ISystemClock clock,
                                                ILogger<DeleteTaskCategoryCommandHandler> logger)
        {
            _categoryRepository = categoryRepository;
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _syncChangeWriter = syncChangeWriter;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteTaskCategoryCommand command,
                                         CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id.
            var currentUserId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 2) Load WITHOUT tracking — use IgnoreQueryFilters internally to detect
            //    already-deleted categories for the idempotency / retry path.
            //    GetByIdUntrackedAsync filters IsDeleted=false, so we call the base
            //    tracked method with query filter ignored via a custom untracked load.
            //    For simplicity, we reload via GetByIdAsync which respects soft-delete,
            //    and handle the "not found" as potential already-deleted case below.
            var category = await _categoryRepository.GetByIdUntrackedAsync(
                command.CategoryId, cancellationToken);

            if (category is null)
            {
                // Could be genuinely not found, or already soft-deleted.
                // Either way, ensure task references are cleared (safe to call on empty set).
                var orphanCleared = await _taskRepository.ClearCategoryFromTasksAsync(
                    command.CategoryId, currentUserId, utcNow, cancellationToken);

                foreach (var task in orphanCleared)
                {
                    await _syncChangeWriter.AddUpdatedAsync(task, originDeviceId: null, cancellationToken);
                }

                if (orphanCleared.Count > 0)
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "DeleteTaskCategory: category {CategoryId} not found (may already be deleted). " +
                    "Cleared any residual task references for user {UserId}.",
                    command.CategoryId, currentUserId);

                return Result.Ok();
            }

            if (category.UserId != currentUserId)
            {
                _logger.LogWarning(
                    "DeleteTaskCategory failed: category {CategoryId} not owned by user {UserId}.",
                    command.CategoryId, currentUserId);

                return Result.Fail(
                    new Error("Category not found.")
                        .WithMetadata("ErrorCode", "Categories.NotFound"));
            }

            // 3) Domain soft-delete.
            var deleteResult = category.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                return deleteResult.ToResult();
            }

            // 4) Create outbox message BEFORE persisting.
            var payload = JsonSerializer.Serialize(new
            {
                CategoryId = category.Id,
                category.UserId,
                category.Name,
                Event = TaskCategoryEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                aggregate: category,
                eventType: TaskCategoryEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure || outboxResult.Value is null)
            {
                return outboxResult.ToResult();
            }

            // 5) Clear CategoryId on all affected tasks (change-tracker pattern; staged, not yet committed).
            //    Returns the mutated entities so we can emit a Task.Updated SyncChange row per task.
            //    Bumps Version so any stale mobile push attempts receive a VersionMismatch conflict
            //    rather than silently re-assigning a deleted category.
            var clearedTasks = await _taskRepository.ClearCategoryFromTasksAsync(
                category.Id, currentUserId, utcNow, cancellationToken);

            // 6) Stage the category soft-delete + outbox + SyncChange rows. All commit atomically
            //    in the single SaveChangesAsync below alongside the staged task mutations from step 5.
            category.ApplyClientRowVersion(command.RowVersion); // REFACTORED: enable stale-page detection
            _categoryRepository.Update(category);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _syncChangeWriter.AddDeletedAsync(SyncEntityFamily.Category, category.Id, currentUserId, originDeviceId: null, cancellationToken);

            foreach (var task in clearedTasks)
            {
                await _syncChangeWriter.AddUpdatedAsync(task, originDeviceId: null, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Category {CategoryId} soft-deleted and task references cleared for user {UserId}.",
                category.Id, currentUserId);

            return Result.Ok();
        }
    }
}
