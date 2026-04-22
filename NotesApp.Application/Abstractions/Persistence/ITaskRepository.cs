using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface ITaskRepository : ICalendarEntityRepository<TaskItem>
    {
        /// <summary>
        /// Returns tasks whose reminder time has passed and for which a reminder
        /// has not yet been sent or acknowledged.
        /// </summary>
        /// <param name="utcNow">Current UTC time used as the "overdue" threshold.</param>
        /// <param name="maxResults">Maximum number of tasks to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<List<TaskItem>> GetOverdueRemindersAsync(DateTime utcNow,
                                                      int maxResults,
                                                      CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk-clears <c>CategoryId</c> on all non-deleted tasks belonging to the user
        /// that reference the given category. Also increments <c>Version</c> and sets
        /// <c>UpdatedAtUtc</c> so that affected tasks surface in the next sync pull and
        /// any stale mobile push attempts receive a <c>VersionMismatch</c> conflict.
        ///
        /// Called only from <c>DeleteTaskCategoryCommandHandler</c> (REST/web path).
        /// In the sync push path, mobile clients send the affected task updates themselves.
        /// </summary>
        /// <param name="categoryId">The category whose reference should be cleared.</param>
        /// <param name="userId">Owner of the tasks (tenant boundary).</param>
        /// <param name="utcNow">Current UTC time applied to UpdatedAtUtc.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ClearCategoryFromTasksAsync(Guid categoryId,
                                         Guid userId,
                                         DateTime utcNow,
                                         CancellationToken cancellationToken = default);

        // REFACTORED: added recurring-task methods for recurring-tasks feature

        /// <summary>
        /// Returns a merged list of materialized TaskItems and projected virtual recurring occurrences
        /// for the given user and day. Replaces direct use of the base GetForDayAsync for task views.
        ///
        /// Implementation:
        /// 1. Load materialized TaskItems for the day (existing EF query).
        /// 2. Load active RecurringTaskSeries overlapping the date.
        /// 3. For series with MaterializedUpToDate &lt; date: call IRecurrenceEngine to project
        ///    virtual occurrences, apply exceptions, and deduplicate against materialized TaskItems
        ///    using CanonicalOccurrenceDate.
        /// 4. Merge and return sorted by StartTime.
        /// </summary>
        Task<IReadOnlyList<TaskOccurrenceResult>> GetOccurrencesForDayAsync(Guid userId,
                                                                            DateOnly date,
                                                                            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a merged list of materialized TaskItems and projected virtual recurring occurrences
        /// for the given user within [<paramref name="fromInclusive"/>, <paramref name="toExclusive"/>).
        /// Same projection logic as <see cref="GetOccurrencesForDayAsync"/>.
        /// </summary>
        Task<IReadOnlyList<TaskOccurrenceResult>> GetOccurrencesForDateRangeAsync(Guid userId,
                                                                                  DateOnly fromInclusive,
                                                                                  DateOnly toExclusive,
                                                                                  CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all materialized TaskItems for the given series within the date range.
        /// Used by "edit all" to identify individually-modified occurrences.
        /// </summary>
        Task<IReadOnlyList<TaskItem>> GetBySeriesInRangeAsync(Guid seriesId,
                                                              DateOnly from,
                                                              DateOnly toExclusive,
                                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all non-deleted materialized TaskItems for the given series across all dates.
        /// Used by "edit all" to update all occurrences that lack an individual exception.
        /// </summary>
        Task<IReadOnlyList<TaskItem>> GetBySeriesAsync(Guid seriesId,
                                                       Guid userId,
                                                       CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all non-deleted materialized TaskItems for the series whose Date &gt;=
        /// <paramref name="fromInclusive"/> into the EF change tracker, calls domain
        /// SoftDelete() on each, and marks them as modified.
        /// Does NOT call SaveChangesAsync(). Caller commits atomically (change-tracker pattern).
        /// </summary>
        Task SoftDeleteRecurringFromDateAsync(Guid seriesId,
                                              DateOnly fromInclusive,
                                              Guid userId,
                                              DateTime utcNow,
                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads all non-deleted materialized TaskItems for any series belonging to the given root
        /// into the EF change tracker, calls domain SoftDelete() on each, and marks them as modified.
        /// Does NOT call SaveChangesAsync(). Caller commits atomically (change-tracker pattern).
        /// </summary>
        Task SoftDeleteAllForRootAsync(Guid rootId,
                                       Guid userId,
                                       DateTime utcNow,
                                       CancellationToken cancellationToken = default);
    }
}
