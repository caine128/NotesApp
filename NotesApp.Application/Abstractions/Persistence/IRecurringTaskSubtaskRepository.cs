using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository for <see cref="RecurringTaskSubtask"/> entities.
    ///
    /// Serves both roles of the dual-FK entity:
    /// - Series template subtasks  (SeriesId set, ExceptionId null)
    /// - Exception subtask overrides (ExceptionId set, SeriesId null)
    ///
    /// No separate repository is needed — the same entity, same table, same CRUD.
    /// Callers distinguish roles by which query method they call.
    /// </summary>
    public interface IRecurringTaskSubtaskRepository : IRepository<RecurringTaskSubtask>
    {
        // -------------------------
        // Series template subtasks
        // -------------------------

        /// <summary>
        /// Returns all non-deleted template subtasks for the given series, ordered by Position.
        /// Used by the materializer and GetVirtualOccurrenceDetail when no exception subtask overrides exist.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSubtask>> GetBySeriesIdAsync(Guid seriesId,
                                                                     CancellationToken cancellationToken = default);

        // -------------------------
        // Exception subtask overrides
        // -------------------------

        /// <summary>
        /// Returns all non-deleted exception subtask overrides for the given exception, ordered by Position.
        /// Non-empty result means this occurrence has its own subtask list (overrides the template).
        /// Empty result means the occurrence should inherit template subtasks.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSubtask>> GetByExceptionIdAsync(Guid exceptionId,
                                                                        CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch-loads all non-deleted exception subtask overrides for multiple exceptions in one query.
        /// Used by the materializer to avoid N+1 queries when processing many exceptions.
        /// Caller groups results by ExceptionId into a dictionary.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSubtask>> GetByExceptionIdsAsync(IReadOnlyList<Guid> exceptionIds,
                                                                         CancellationToken cancellationToken = default);

        // -------------------------
        // Sync pull
        // -------------------------

        /// <summary>
        /// Returns all recurring subtask rows for the given user that have changed since the
        /// specified timestamp. Covers both series template and exception subtask rows.
        ///
        /// Semantics:
        /// - When <paramref name="since"/> is null: returns all non-deleted rows (initial sync).
        /// - When <paramref name="since"/> is not null: returns all rows (including soft-deleted)
        ///   where UpdatedAtUtc &gt; since (incremental sync).
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSubtask>> GetChangedSinceAsync(Guid userId,
                                                                       DateTime? since,
                                                                       CancellationToken cancellationToken = default);
    }
}
