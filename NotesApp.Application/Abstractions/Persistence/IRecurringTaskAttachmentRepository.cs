using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository abstraction for <see cref="RecurringTaskAttachment"/> entities.
    ///
    /// Covers both series template attachments (SeriesId set) and
    /// exception attachment overrides (ExceptionId set).
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public interface IRecurringTaskAttachmentRepository : IRepository<RecurringTaskAttachment>
    {
        /// <summary>
        /// Returns all non-deleted series template attachments for the given series,
        /// ordered by <see cref="RecurringTaskAttachment.DisplayOrder"/> ascending.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskAttachment>> GetBySeriesIdAsync(
            Guid seriesId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the count of non-deleted series template attachments for the given series.
        /// Used to enforce the attachment count limit and compute the next DisplayOrder.
        /// </summary>
        Task<int> CountForSeriesAsync(
            Guid seriesId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk soft-deletes all non-deleted series template attachments for the given series.
        /// Uses the change-tracker pattern — caller's <c>SaveChangesAsync()</c> commits atomically.
        /// </summary>
        Task SoftDeleteAllForSeriesAsync(
            Guid seriesId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all non-deleted exception attachment overrides for the given exception,
        /// ordered by <see cref="RecurringTaskAttachment.DisplayOrder"/> ascending.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskAttachment>> GetByExceptionIdAsync(
            Guid exceptionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the count of non-deleted exception attachment overrides for the given exception.
        /// </summary>
        Task<int> CountForExceptionAsync(
            Guid exceptionId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk soft-deletes all non-deleted exception attachment overrides for the given exception.
        /// Uses the change-tracker pattern — caller's <c>SaveChangesAsync()</c> commits atomically.
        /// Required because the ExceptionId FK uses DeleteBehavior.Restrict (no DB-level cascade).
        /// </summary>
        Task SoftDeleteAllForExceptionAsync(
            Guid exceptionId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch-loads non-deleted exception attachment overrides for a set of exception IDs.
        /// Returns an empty list when <paramref name="exceptionIds"/> is empty.
        /// Used by sync pull to avoid N+1 queries.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskAttachment>> GetByExceptionIdsAsync(
            IReadOnlyList<Guid> exceptionIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns attachments that have changed since the given timestamp (for sync pull).
        ///
        /// Semantics:
        /// - When <paramref name="sinceUtc"/> is null: returns all non-deleted attachments for the user.
        /// - When <paramref name="sinceUtc"/> has a value: returns all attachments (including soft-deleted)
        ///   where UserId == userId AND UpdatedAtUtc &gt; sinceUtc.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskAttachment>> GetChangedSinceAsync(
            Guid userId,
            DateTime? sinceUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns non-deleted attachments whose parent series or exception is soft-deleted.
        /// Used by the background orphan-cleanup worker to identify blobs for deletion.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskAttachment>> GetOrphanRecurringAttachmentsAsync(
            int limit,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns true if any non-deleted <see cref="RecurringTaskAttachment"/> references
        /// the given blob path (shared-blob guard for the orphan-cleanup worker).
        /// ThisAndFollowing attachment copies share the same BlobPath across series splits.
        /// </summary>
        Task<bool> ExistsNonDeletedWithBlobPathAsync(
            string blobPath,
            CancellationToken cancellationToken = default);
    }
}
