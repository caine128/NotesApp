using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    public sealed record TaskSummaryDto(Guid TaskId,
                                        string Title,
                                        DateOnly Date,
                                        TimeOnly? StartTime,
                                        TimeOnly? EndTime,
                                        bool IsCompleted,
                                        string? Location,
                                        TimeSpan? TravelTime,
                                        Guid? CategoryId,
                                        TaskPriority Priority, // REFACTORED: added Priority for task priority feature
                                        string? MeetingLink)  // REFACTORED: added MeetingLink for meeting-link feature
    {
        // REFACTORED: added recurring-task fields for recurring-tasks feature.
        // Defined as init properties (not positional) so existing callsites are unchanged.

        /// <summary>
        /// Id of the RecurringTaskSeries that generated this occurrence.
        /// Null for non-recurring tasks.
        /// </summary>
        public Guid? RecurringSeriesId { get; init; }

        /// <summary>
        /// The recurrence-engine-generated date for this occurrence, before any user-applied date move.
        /// Null for non-recurring tasks.
        /// Together with RecurringSeriesId, uniquely identifies the occurrence for edit/delete operations.
        /// </summary>
        public DateOnly? CanonicalOccurrenceDate { get; init; }

        /// <summary>
        /// True when this occurrence is projected at query time from a RecurringTaskSeries
        /// and has not yet been materialized into a TaskItem row.
        /// When true: TaskItemId is null and the client must use the virtual-occurrence endpoints.
        /// </summary>
        public bool IsVirtualOccurrence { get; init; }
    }
}
