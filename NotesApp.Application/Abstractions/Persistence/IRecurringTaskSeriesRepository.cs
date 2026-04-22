using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Repository for <see cref="RecurringTaskSeries"/> entities.
    ///
    /// Design notes:
    /// - GetActiveByRootIdAsync belongs here (not on IRecurringTaskRootRepository) because
    ///   it returns Series entities — each repository owns queries that return its own type.
    /// - Bulk soft-delete methods use the change-tracker pattern: they load entities into the
    ///   EF change tracker, call domain SoftDelete() on each, and rely on the caller's
    ///   SaveChangesAsync() for atomic persistence. ExecuteUpdateAsync() is NOT used here.
    /// </summary>
    public interface IRecurringTaskSeriesRepository : IRepository<RecurringTaskSeries>
    {
        /// <summary>
        /// Returns all non-deleted series segments that belong to the given root.
        /// Used by "edit all" and "delete all" operations.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSeries>> GetActiveByRootIdAsync(Guid rootId,
                                                                        Guid userId,
                                                                        CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all non-deleted series segments whose MaterializedUpToDate is before
        /// <paramref name="targetDate"/>, up to <paramref name="batchSize"/> results.
        /// Used by the horizon worker to find series that need materialization.
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSeries>> GetSeriesBehindHorizonAsync(DateOnly targetDate,
                                                                             int batchSize,
                                                                             CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all non-deleted series segments for the given user whose date range overlaps
        /// [<paramref name="from"/>, <paramref name="toExclusive"/>).
        /// Used by GetForDayAsync / GetForDateRangeAsync to project virtual occurrences.
        /// A series overlaps when StartsOnDate &lt; toExclusive AND
        /// (EndsBeforeDate IS NULL OR EndsBeforeDate &gt; from).
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSeries>> GetOverlappingDateRangeAsync(Guid userId,
                                                                              DateOnly from,
                                                                              DateOnly toExclusive,
                                                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all series for the given user that have changed since the specified timestamp.
        ///
        /// Semantics:
        /// - When <paramref name="since"/> is null: returns all non-deleted series (initial sync).
        /// - When <paramref name="since"/> is not null: returns all series (including soft-deleted)
        ///   where UpdatedAtUtc &gt; since (incremental sync).
        /// </summary>
        Task<IReadOnlyList<RecurringTaskSeries>> GetChangedSinceAsync(Guid userId,
                                                                      DateTime? since,
                                                                      CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all non-deleted series for the root into the EF change tracker,
        /// calls <see cref="RecurringTaskSeries.SoftDelete"/> on each, and marks them
        /// as modified so the caller's single SaveChangesAsync() persists everything atomically.
        /// Does NOT call SaveChangesAsync() itself.
        /// </summary>
        Task SoftDeleteAllForRootAsync(
            Guid rootId,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken = default);
    }
}
