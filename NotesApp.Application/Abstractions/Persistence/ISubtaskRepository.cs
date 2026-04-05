using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for <see cref="Subtask"/> entities.
    ///
    /// Subtasks are not date-scoped, so this interface extends <see cref="IRepository{TEntity}"/>
    /// directly rather than <see cref="ICalendarEntityRepository{TEntity}"/>
    /// (same reasoning as <see cref="ICategoryRepository"/>).
    /// </summary>
    public interface ISubtaskRepository : IRepository<Subtask>
    {
        /// <summary>
        /// Returns all non-deleted subtasks owned by the given user for the specified task,
        /// ordered by Position (fractional index).
        /// </summary>
        Task<IReadOnlyList<Subtask>> GetAllForTaskAsync(
            Guid taskId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns subtasks that have changed since the given timestamp (for sync pull).
        ///
        /// Semantics:
        /// - When <paramref name="sinceUtc"/> is null:
        ///   Returns all non-deleted subtasks for the user (initial sync).
        /// - When <paramref name="sinceUtc"/> has a value:
        ///   Returns all subtasks (including soft-deleted) where
        ///   UserId == userId AND UpdatedAtUtc &gt; sinceUtc.
        ///
        /// The caller is responsible for categorising results into
        /// created / updated / deleted buckets.
        /// </summary>
        Task<IReadOnlyList<Subtask>> GetChangedSinceAsync(
            Guid userId,
            DateTime? sinceUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk soft-deletes all non-deleted subtasks belonging to the given task.
        /// Increments Version and sets UpdatedAtUtc so that cascade-deleted subtasks
        /// surface in the next sync pull.
        ///
        /// Called from:
        /// - <c>DeleteTaskCommandHandler</c> (REST delete path).
        /// - <c>SyncPushCommandHandler.ProcessTaskDeletesAsync</c> (sync push safety sweep).
        /// </summary>
        Task SoftDeleteAllForTaskAsync(
            Guid taskId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default);
    }
}
