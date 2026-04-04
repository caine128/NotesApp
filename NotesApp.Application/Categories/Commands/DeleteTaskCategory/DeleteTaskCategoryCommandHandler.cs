using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
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
    /// - Bulk-clears CategoryId on all affected tasks (ClearCategoryFromTasksAsync) FIRST,
    ///   incrementing their Version so stale mobile push attempts receive a VersionMismatch.
    /// - Then commits the soft-delete and outbox atomically via SaveChangesAsync.
    ///
    /// Ordering rationale (clear BEFORE save):
    ///   If ClearCategoryFromTasksAsync fails  → category is still live; safe to retry from scratch.
    ///   If SaveChangesAsync fails after clear  → tasks are clean, category still live;
    ///                                            next retry finds 0 tasks to clear, then saves.
    ///   Neither failure mode leaves orphaned task FK rows.
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
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteTaskCategoryCommandHandler> _logger;

        public DeleteTaskCategoryCommandHandler(
            ICategoryRepository categoryRepository,
            ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteTaskCategoryCommandHandler> logger)
        {
            _categoryRepository = categoryRepository;
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(
            DeleteTaskCategoryCommand command,
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
                await _taskRepository.ClearCategoryFromTasksAsync(
                    command.CategoryId, currentUserId, utcNow, cancellationToken);

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

            // 5) Bulk-clear CategoryId on all affected tasks BEFORE committing the soft-delete.
            //    If this call fails, the category is still live and the operation is safe to retry
            //    from scratch — no orphaned FK rows are possible.
            //    Increments Version on affected tasks so any stale mobile push attempts
            //    receive a VersionMismatch conflict rather than silently re-assigning a
            //    deleted category.
            await _taskRepository.ClearCategoryFromTasksAsync(
                category.Id, currentUserId, utcNow, cancellationToken);

            // 6) Persist the soft-delete and outbox atomically.
            //    Runs AFTER the task clear so failure here leaves tasks already clean.
            //    The next retry will find 0 tasks to clear (no-op) and then save successfully.
            _categoryRepository.Update(category);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Category {CategoryId} soft-deleted and task references cleared for user {UserId}.",
                category.Id, currentUserId);

            return Result.Ok();
        }
    }
}
