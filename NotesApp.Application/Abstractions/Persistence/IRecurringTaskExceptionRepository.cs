using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository for <see cref="RecurringTaskException"/> entities.
    ///
    /// Design notes:
    /// - Bulk soft-delete methods (SoftDeleteFromDateAsync, SoftDeleteAllForRootAsync) use the
    ///   change-tracker pattern: load entities into EF change tracker → call domain SoftDelete() →
    ///   caller's SaveChangesAsync() commits atomically. ExecuteUpdateAsync() is NOT used.
    /// - No subtask child-row methods: exception subtasks are RecurringTaskSubtask rows loaded
    ///   via IRecurringTaskSubtaskRepository.GetByExceptionIdAsync (separate repository calls,
    ///   no EF navigation properties — consistent with existing conventions).
    /// </summary>
    public interface IRecurringTaskExceptionRepository : IRepository<RecurringTaskException>
    {
        /// <summary>
        /// Returns the single active exception for the given (SeriesId, OccurrenceDate) pair,
        /// or null if none exists.
        /// Used by command handlers to upsert exceptions (create or update).
        /// </summary>
        Task<RecurringTaskException?> GetByOccurrenceAsync(Guid seriesId,
                                                           DateOnly occurrenceDate,
                                                           CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all active exceptions whose OccurrenceDate falls within
        /// [<paramref name="from"/>, <paramref name="toExclusive"/>).
        /// Used by the TaskRepository projection and the materializer.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskException>> GetForSeriesInRangeAsync(Guid seriesId,
                                                                             DateOnly from,
                                                                             DateOnly toExclusive,
                                                                             CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all exceptions for the given user that have changed since the specified timestamp.
        ///
        /// Semantics:
        /// - When <paramref name="since"/> is null: returns all non-deleted exceptions (initial sync).
        /// - When <paramref name="since"/> is not null: returns all exceptions (including soft-deleted)
        ///   where UpdatedAtUtc &gt; since (incremental sync).
        /// </summary>
        Task<IReadOnlyList<RecurringTaskException>> GetChangedSinceAsync(Guid userId,
                                                                         DateTime? since,
                                                                         CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all non-deleted exceptions for the series whose OccurrenceDate &gt;=
        /// <paramref name="fromInclusive"/> into the EF change tracker, calls domain
        /// SoftDelete() on each, and marks them as modified.
        /// Does NOT call SaveChangesAsync(). Caller commits atomically.
        /// </summary>
        Task SoftDeleteFromDateAsync(Guid seriesId,
                                     DateOnly fromInclusive,
                                     Guid userId,
                                     DateTime utcNow,
                                     CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all non-deleted exceptions across all series belonging to the given root into
        /// the EF change tracker, calls domain SoftDelete() on each, and marks them as modified.
        /// Does NOT call SaveChangesAsync(). Caller commits atomically.
        /// </summary>
        Task SoftDeleteAllForRootAsync(Guid rootId,
                                       Guid userId,
                                       DateTime utcNow,
                                       CancellationToken cancellationToken = default);
    }
}
