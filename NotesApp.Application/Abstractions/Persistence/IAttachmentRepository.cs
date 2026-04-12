using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for <see cref="Attachment"/> entities.
    ///
    /// Attachments are not date-scoped, so this interface extends <see cref="IRepository{TEntity}"/>
    /// directly — the same reasoning as <see cref="ISubtaskRepository"/>.
    /// </summary>
    public interface IAttachmentRepository : IRepository<Attachment>
    {
        /// <summary>
        /// Returns all non-deleted attachments owned by the given user for the specified task,
        /// ordered by <see cref="Attachment.DisplayOrder"/> ascending.
        /// </summary>
        Task<IReadOnlyList<Attachment>> GetAllForTaskAsync(
            Guid taskId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the count of non-deleted attachments for the specified task.
        ///
        /// Used to enforce the <c>MaxAttachmentsPerTask</c> limit and to compute
        /// the next <see cref="Attachment.DisplayOrder"/> value before creating an attachment.
        /// </summary>
        Task<int> CountForTaskAsync(
            Guid taskId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns attachments that have changed since the given timestamp (for sync pull).
        ///
        /// Semantics:
        /// - When <paramref name="sinceUtc"/> is null:
        ///   Returns all non-deleted attachments for the user (initial sync).
        /// - When <paramref name="sinceUtc"/> has a value:
        ///   Returns all attachments (including soft-deleted) where
        ///   UserId == userId AND UpdatedAtUtc &gt; sinceUtc.
        ///
        /// The caller is responsible for categorising results into created / deleted buckets.
        /// </summary>
        Task<IReadOnlyList<Attachment>> GetChangedSinceAsync(
            Guid userId,
            DateTime? sinceUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk soft-deletes all non-deleted attachments belonging to the given task.
        /// Sets UpdatedAtUtc so that cascade-deleted attachments surface in the next sync pull.
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

        /// <summary>
        /// Returns non-deleted attachments whose parent task is soft-deleted.
        /// Used by the background orphan-cleanup worker to delete the corresponding blobs.
        /// </summary>
        Task<IReadOnlyList<Attachment>> GetOrphanAttachmentsAsync(
            int limit,
            CancellationToken cancellationToken = default);
    }
}
