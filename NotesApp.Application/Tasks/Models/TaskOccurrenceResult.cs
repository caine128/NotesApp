using NotesApp.Domain.Common;
using System;

namespace NotesApp.Application.Tasks.Models
{
    /// <summary>
    /// Unified projection record returned by GetOccurrencesForDayAsync /
    /// GetOccurrencesForDateRangeAsync on <see cref="ITaskRepository"/>.
    /// Merges two kinds of occurrences:
    ///
    /// 1. <b>Materialized occurrence</b> (<see cref="IsVirtualOccurrence"/> = false):
    ///    Backed by a real <see cref="TaskItem"/> row. All fields are populated.
    ///
    /// 2. <b>Virtual occurrence</b> (<see cref="IsVirtualOccurrence"/> = true):
    ///    Projected at query time from a <see cref="RecurringTaskSeries"/> template +
    ///    any applicable <see cref="RecurringTaskException"/> override fields.
    ///    <see cref="IsCompleted"/> and <see cref="ReminderAtUtc"/> are populated from the
    ///    exception (if one exists) and are meaningful for virtual occurrences.
    ///    <see cref="TaskItemId"/> and <see cref="RowVersion"/> are null — no TaskItem row exists yet.
    /// </summary>
    public sealed record TaskOccurrenceResult
    {
        // -------------------------
        // Always populated
        // -------------------------

        /// <summary>The calendar date on which this occurrence appears.</summary>
        public DateOnly Date { get; init; }

        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public Guid? CategoryId { get; init; }
        public TaskPriority Priority { get; init; }
        public string? MeetingLink { get; init; }

        /// <summary>
        /// True when this occurrence is projected from a recurrence engine and has not yet
        /// been materialized into a <see cref="TaskItem"/> row.
        /// </summary>
        public bool IsVirtualOccurrence { get; init; }

        /// <summary>
        /// Completion state.
        /// For materialized occurrences: taken directly from <see cref="TaskItem.IsCompleted"/>.
        /// For virtual occurrences: taken from <see cref="RecurringTaskException.IsCompleted"/>
        /// when an exception exists; false otherwise (no exception = not yet interacted with).
        /// </summary>
        public bool IsCompleted { get; init; }

        /// <summary>
        /// Reminder timestamp in UTC.
        /// For materialized occurrences: taken directly from <see cref="TaskItem.ReminderAtUtc"/>.
        /// For virtual occurrences: computed from the exception's absolute override (if set) or
        /// from <see cref="RecurringTaskSeries.ReminderOffsetMinutes"/> + occurrence date + start time.
        /// Null when no reminder is configured.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }

        // -------------------------
        // Populated when IsVirtualOccurrence = false (materialized only)
        // -------------------------

        /// <summary>Id of the underlying TaskItem. Null for virtual occurrences.</summary>
        public Guid? TaskItemId { get; init; }

        /// <summary>
        /// EF Core concurrency token for the TaskItem. Null for virtual occurrences.
        /// Required for optimistic concurrency when submitting edits to a materialized occurrence.
        /// </summary>
        public byte[]? RowVersion { get; init; }

        // -------------------------
        // Populated for all recurring occurrences (both materialized and virtual)
        // -------------------------

        /// <summary>
        /// Id of the RecurringTaskSeries that owns this occurrence.
        /// Null for non-recurring tasks.
        /// </summary>
        public Guid? RecurringSeriesId { get; init; }

        /// <summary>
        /// The recurrence-engine-generated date for this occurrence, before any user-applied
        /// date move. Null for non-recurring tasks.
        /// Used by the client to look up the correct exception via (SeriesId, CanonicalOccurrenceDate).
        /// </summary>
        public DateOnly? CanonicalOccurrenceDate { get; init; }
    }
}
