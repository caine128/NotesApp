using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Services;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    public sealed class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _context;
        // REFACTORED: added for recurring-tasks feature
        private readonly IRecurrenceEngine _recurrenceEngine;

        public TaskRepository(AppDbContext context, IRecurrenceEngine recurrenceEngine)
        {
            _context = context;
            _recurrenceEngine = recurrenceEngine;
        }

        // Generic repository methods

        public async Task<TaskItem?> GetByIdAsync(Guid id,
                                                  CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }

        public async Task<TaskItem?> GetByIdUntrackedAsync(Guid id,
                                                           CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }

        public async Task AddAsync(TaskItem entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.Tasks.AddAsync(entity, cancellationToken);
        }

        public void Update(TaskItem entity)
        {
            _context.Tasks.Update(entity);
        }

        public void Remove(TaskItem entity)
        {
            _context.Tasks.Remove(entity);
        }


        // Task-specific query methods

        public async Task<IReadOnlyList<TaskItem>> GetForDayAsync(Guid userId,
                                                                  DateOnly date,
                                                                  CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.Date == date
                            && !t.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TaskItem>> GetForDateRangeAsync(Guid userId,
                                                                        DateOnly fromInclusive,
                                                                        DateOnly toExclusive,
                                                                        CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.Date >= fromInclusive
                            && t.Date < toExclusive
                            && !t.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskItem>> GetChangedSinceAsync(Guid userId,
                                                                        DateTime? since,
                                                                        CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                // Initial sync: all non-deleted tasks for the user.
                return await _context.Tasks
                    .Where(t => t.UserId == userId && !t.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted tasks as well.
            return await _context.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        // REFACTORED: added ClearCategoryFromTasksAsync for task categories feature

        /// <inheritdoc />
        public async Task ClearCategoryFromTasksAsync(Guid categoryId,
                                                      Guid userId,
                                                      DateTime utcNow,
                                                      CancellationToken cancellationToken = default)
        {
            // Bulk UPDATE using ExecuteUpdateAsync — does NOT load entities into memory.
            // Increments Version so that any stale push attempt from a mobile client
            // that still holds the old CategoryId receives a VersionMismatch conflict.
            await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.CategoryId == categoryId
                            && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.CategoryId, (Guid?)null)
                    .SetProperty(t => t.Version, t => t.Version + 1)
                    .SetProperty(t => t.UpdatedAtUtc, utcNow),
                    cancellationToken);
        }

        public async Task<List<TaskItem>> GetOverdueRemindersAsync(DateTime utcNow,
                                                                   int maxResults,
                                                                   CancellationToken cancellationToken = default)
        {
            if (maxResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults,
                    "maxResults must be greater than zero.");
            }

            // A reminder is "overdue" when:
            // - The task is not soft-deleted
            // - ReminderAtUtc is set and <= utcNow
            // - ReminderSentAtUtc is null (we haven't sent it yet)
            // - ReminderAcknowledgedAtUtc is null (user hasn't acknowledged)
            return await _context.Tasks
                .Where(t =>
                    !t.IsDeleted &&
                    t.ReminderAtUtc != null &&
                    t.ReminderAtUtc <= utcNow &&
                    t.ReminderSentAtUtc == null &&
                    t.ReminderAcknowledgedAtUtc == null)
                .OrderBy(t => t.ReminderAtUtc)
                .Take(maxResults)
                .ToListAsync(cancellationToken);
        }

        // REFACTORED: added recurring-task methods for recurring-tasks feature

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskOccurrenceResult>> GetOccurrencesForDayAsync(Guid userId,
                                                                                         DateOnly date,
                                                                                         CancellationToken cancellationToken = default)
        {
            return await GetOccurrencesForRangeInternalAsync(
                userId, date, date.AddDays(1), cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskOccurrenceResult>> GetOccurrencesForDateRangeAsync(Guid userId,
                                                                                               DateOnly fromInclusive,
                                                                                               DateOnly toExclusive,
                                                                                               CancellationToken cancellationToken = default)
        {
            return await GetOccurrencesForRangeInternalAsync(
                userId, fromInclusive, toExclusive, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskItem>> GetBySeriesInRangeAsync(Guid seriesId,
                                                                           DateOnly from,
                                                                           DateOnly toExclusive,
                                                                           CancellationToken cancellationToken = default)
        {
            // Filter by CanonicalOccurrenceDate (engine-assigned position in the recurrence sequence),
            // NOT by Date (displayed date). A user may have moved an occurrence forward or backward
            // via a Single edit — CanonicalOccurrenceDate is the stable identity that places the
            // task within the series timeline regardless of any display-date override.
            return await _context.Tasks
                .Where(t => t.RecurringSeriesId == seriesId
                            && t.CanonicalOccurrenceDate >= from
                            && t.CanonicalOccurrenceDate < toExclusive)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskItem>> GetBySeriesAsync(Guid seriesId,
                                                                    Guid userId,
                                                                    CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.RecurringSeriesId == seriesId && t.UserId == userId)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SoftDeleteRecurringFromDateAsync(Guid seriesId,
                                                           DateOnly fromInclusive,
                                                           Guid userId,
                                                           DateTime utcNow,
                                                           CancellationToken cancellationToken = default)
        {
            // Change-tracker pattern: load entities, call domain SoftDelete() on each.
            // The caller's SaveChangesAsync() commits all changes atomically.
            //
            // Filter by CanonicalOccurrenceDate, NOT by Date.
            // A user may have moved an individual occurrence backward in time via a Single edit
            // (e.g. canonical Apr 21 → displayed Apr 18). Filtering by Date would miss that task
            // on a ThisAndFollowing split at Apr 21, leaving it as a zombie linked to the terminated
            // series. CanonicalOccurrenceDate is the stable identity that places the task within
            // the recurrence sequence regardless of any display-date override.
            var tasks = await _context.Tasks
                .Where(t => t.RecurringSeriesId == seriesId
                            && t.UserId == userId
                            && t.CanonicalOccurrenceDate >= fromInclusive)
                .ToListAsync(cancellationToken);

            foreach (var task in tasks)
            {
                task.SoftDelete(utcNow);
            }
        }

        /// <inheritdoc />
        public async Task SoftDeleteAllForRootAsync(Guid rootId,
                                                    Guid userId,
                                                    DateTime utcNow,
                                                    CancellationToken cancellationToken = default)
        {
            // Resolve series IDs for the root, then soft-delete all their materialized TaskItems.
            var seriesIds = await _context.RecurringTaskSeries
                .IgnoreQueryFilters()
                .Where(s => s.RootId == rootId && s.UserId == userId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            if (seriesIds.Count == 0)
            {
                return;
            }

            var tasks = await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.RecurringSeriesId != null
                            && seriesIds.Contains(t.RecurringSeriesId.Value))
                .ToListAsync(cancellationToken);

            foreach (var task in tasks)
            {
                task.SoftDelete(utcNow);
            }
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Core projection logic shared by GetOccurrencesForDayAsync and
        /// GetOccurrencesForDateRangeAsync.
        ///
        /// Steps:
        /// 1. Load materialized TaskItems in the window — wrap each as a TaskOccurrenceResult.
        /// 2. Load active RecurringTaskSeries that overlap the window.
        /// 3. For each series where MaterializedUpToDate &lt; toExclusive (virtual zone exists):
        ///    a. Load exceptions for the series in the window.
        ///    b. Build a lookup of canonical dates already covered by materialized tasks.
        ///    c. Call IRecurrenceEngine to generate occurrence dates.
        ///    d. Skip deletion exceptions. Apply override fields for override exceptions.
        ///    e. Skip canonical dates that already have a materialized TaskItem.
        ///    f. Project remaining dates as virtual TaskOccurrenceResult entries.
        /// 4. Merge and sort by StartTime (nulls last), then by Title.
        /// </summary>
        private async Task<IReadOnlyList<TaskOccurrenceResult>> GetOccurrencesForRangeInternalAsync(Guid userId,
                                                                                                    DateOnly fromInclusive,
                                                                                                    DateOnly toExclusive,
                                                                                                    CancellationToken cancellationToken)
        {
            var results = new List<TaskOccurrenceResult>();

            // Step 1: Materialized TaskItems
            var materializedTasks = await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.Date >= fromInclusive
                            && t.Date < toExclusive)
                .ToListAsync(cancellationToken);

            foreach (var task in materializedTasks)
            {
                results.Add(MapToOccurrenceResult(task));
            }

            // Step 2: Active series overlapping the window
            var activeSeries = await _context.RecurringTaskSeries
                .Where(s => s.UserId == userId
                            && s.StartsOnDate < toExclusive
                            && (s.EndsBeforeDate == null || s.EndsBeforeDate > fromInclusive))
                .ToListAsync(cancellationToken);

            if (activeSeries.Count == 0)
            {
                return SortOccurrences(results);
            }

            // Build a lookup of canonical occurrence dates already covered by materialized tasks,
            // grouped by series ID — used to skip duplicate virtual projections.
            var coveredCanonicalDates = materializedTasks
                .Where(t => t.RecurringSeriesId.HasValue && t.CanonicalOccurrenceDate.HasValue)
                .GroupBy(t => t.RecurringSeriesId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => new HashSet<DateOnly>(g.Select(t => t.CanonicalOccurrenceDate!.Value)));

            // Step 3: Project virtual occurrences for each series
            foreach (var series in activeSeries)
            {
                // Only project virtual occurrences beyond the materialized horizon.
                // If the whole window is within the materialized zone, no virtual occurrences exist.
                var virtualFrom = series.MaterializedUpToDate >= fromInclusive
                    ? series.MaterializedUpToDate.AddDays(1)
                    : fromInclusive;

                if (virtualFrom >= toExclusive)
                {
                    continue; // Entire window is within the materialized horizon — nothing to project.
                }

                // Step 3a: Load exceptions for this series in the virtual zone
                var exceptions = await _context.RecurringTaskExceptions
                    .Where(e => e.SeriesId == series.Id
                                && e.OccurrenceDate >= virtualFrom
                                && e.OccurrenceDate < toExclusive)
                    .ToListAsync(cancellationToken);

                var deletionDates = new HashSet<DateOnly>(
                    exceptions.Where(e => e.IsDeletion).Select(e => e.OccurrenceDate));

                var overridesByDate = exceptions
                    .Where(e => !e.IsDeletion)
                    .ToDictionary(e => e.OccurrenceDate);

                // Step 3b: Canonical dates already covered by materialized tasks for this series
                coveredCanonicalDates.TryGetValue(series.Id, out var coveredForSeries);

                // Step 3c: Generate occurrence dates via the recurrence engine
                var occurrenceDates = _recurrenceEngine.GenerateOccurrences(
                    series.RRuleString,
                    series.StartsOnDate,
                    series.EndsBeforeDate,
                    virtualFrom,
                    toExclusive);

                foreach (var canonicalDate in occurrenceDates)
                {
                    // Step 3d: Skip deletion exceptions
                    if (deletionDates.Contains(canonicalDate))
                    {
                        continue;
                    }

                    // Step 3e: Skip if already covered by a materialized TaskItem
                    if (coveredForSeries != null && coveredForSeries.Contains(canonicalDate))
                    {
                        continue;
                    }

                    // Step 3f: Apply exception override fields or fall back to series template
                    overridesByDate.TryGetValue(canonicalDate, out var exception);
                    results.Add(ProjectVirtualOccurrence(series, canonicalDate, exception));
                }
            }

            // Step 4: Sort and return
            return SortOccurrences(results);
        }

        private static TaskOccurrenceResult MapToOccurrenceResult(TaskItem task)
        {
            return new TaskOccurrenceResult
            {
                Date = task.Date,
                Title = task.Title,
                Description = task.Description,
                StartTime = task.StartTime,
                EndTime = task.EndTime,
                Location = task.Location,
                TravelTime = task.TravelTime,
                CategoryId = task.CategoryId,
                Priority = task.Priority,
                MeetingLink = task.MeetingLink,
                IsVirtualOccurrence = false,
                TaskItemId = task.Id,
                IsCompleted = task.IsCompleted,
                ReminderAtUtc = task.ReminderAtUtc,
                RowVersion = task.RowVersion,
                RecurringSeriesId = task.RecurringSeriesId,
                CanonicalOccurrenceDate = task.CanonicalOccurrenceDate
            };
        }

        private static TaskOccurrenceResult ProjectVirtualOccurrence(
            RecurringTaskSeries series,
            DateOnly canonicalDate,
            RecurringTaskException? exception)
        {
            // For each field: use the exception override if present, else the series template.
            var date        = exception?.OverrideDate        ?? canonicalDate;
            var title       = exception?.OverrideTitle       ?? series.Title;
            var description = exception?.OverrideDescription ?? series.Description;
            var startTime   = exception?.OverrideStartTime   ?? series.StartTime;
            var endTime     = exception?.OverrideEndTime     ?? series.EndTime;
            var location    = exception?.OverrideLocation    ?? series.Location;
            var travelTime  = exception?.OverrideTravelTime  ?? series.TravelTime;
            var categoryId  = exception?.OverrideCategoryId  ?? series.CategoryId;
            var priority    = exception?.OverridePriority    ?? series.Priority;
            var meetingLink = exception?.OverrideMeetingLink ?? series.MeetingLink;

            // IsCompleted is stored on the exception when one exists; defaults to false otherwise.
            var isCompleted = exception?.IsCompleted ?? false;

            // ReminderAtUtc: use the exception's absolute override if set; otherwise compute from
            // the series offset (same formula as RecurringTaskMaterializerService and
            // GetVirtualTaskOccurrenceDetailQueryHandler).
            var reminderAtUtc = RecurringReminderHelper.ComputeReminderUtc(
                overrideReminderAtUtc: exception?.OverrideReminderAtUtc,
                reminderOffsetMinutes: series.ReminderOffsetMinutes,
                occurrenceDate: date,
                startTime: startTime);

            return new TaskOccurrenceResult
            {
                Date = date,
                Title = title,
                Description = description,
                StartTime = startTime,
                EndTime = endTime,
                Location = location,
                TravelTime = travelTime,
                CategoryId = categoryId,
                Priority = priority,
                MeetingLink = meetingLink,
                IsVirtualOccurrence = true,
                TaskItemId = null,
                IsCompleted = isCompleted,
                ReminderAtUtc = reminderAtUtc,
                RowVersion = null,
                RecurringSeriesId = series.Id,
                CanonicalOccurrenceDate = canonicalDate
            };
        }

        private static IReadOnlyList<TaskOccurrenceResult> SortOccurrences(
            List<TaskOccurrenceResult> results)
        {
            return results
                .OrderBy(r => r.StartTime.HasValue ? 0 : 1) // timed tasks before untimed
                .ThenBy(r => r.StartTime)
                .ThenBy(r => r.Title)
                .ToList();
        }
    }
}
