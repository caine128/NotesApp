using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;


namespace NotesApp.Application.Tasks
{
    public static class TaskMappings
    {
        public static TaskDetailDto ToDetailDto(this TaskItem task) =>
          new(task.Id,
              task.Title,
              task.Description,
              task.Date,
              task.StartTime,
              task.EndTime,
              task.IsCompleted,
              task.Location,
              task.TravelTime,
              task.CreatedAtUtc,
              task.UpdatedAtUtc,
              task.ReminderAtUtc,
              task.CategoryId,
              task.Priority, // REFACTORED: added Priority
              task.MeetingLink, // REFACTORED: added MeetingLink
              task.RowVersion); // REFACTORED: added RowVersion for web concurrency protection

        // -------------------------------------------------------------------------
        // TaskItem → TaskSummaryDto (non-recurring path — existing behavior)
        // -------------------------------------------------------------------------

        public static TaskSummaryDto ToSummaryDto(this TaskItem task) =>
            new(
                task.Id,
                task.Title,
                task.Date,
                task.StartTime,
                task.EndTime,
                task.IsCompleted,
                task.Location,
                task.TravelTime,
                task.CategoryId,
                task.Priority, // REFACTORED: added Priority
                task.MeetingLink) // REFACTORED: added MeetingLink
            {
                // REFACTORED: populate recurring fields for materialized recurring tasks
                RecurringSeriesId = task.RecurringSeriesId,
                CanonicalOccurrenceDate = task.CanonicalOccurrenceDate,
                IsVirtualOccurrence = false
            };

        // -------------------------------------------------------------------------
        // TaskOccurrenceResult → TaskSummaryDto
        // REFACTORED: new overload for recurring-tasks feature
        // -------------------------------------------------------------------------

        /// <summary>
        /// Maps a <see cref="TaskOccurrenceResult"/> (merged materialized + virtual occurrence)
        /// to a <see cref="TaskSummaryDto"/>.
        /// </summary>
        public static TaskSummaryDto ToSummaryDto(this TaskOccurrenceResult occurrence) =>
            new(
                TaskId: occurrence.TaskItemId ?? System.Guid.Empty,
                Title: occurrence.Title,
                Date: occurrence.Date,
                StartTime: occurrence.StartTime,
                EndTime: occurrence.EndTime,
                IsCompleted: occurrence.IsCompleted,
                Location: occurrence.Location,
                TravelTime: occurrence.TravelTime,
                CategoryId: occurrence.CategoryId,
                Priority: occurrence.Priority,
                MeetingLink: occurrence.MeetingLink)
            {
                RecurringSeriesId = occurrence.RecurringSeriesId,
                CanonicalOccurrenceDate = occurrence.CanonicalOccurrenceDate,
                IsVirtualOccurrence = occurrence.IsVirtualOccurrence
            };

        // -------------------------------------------------------------------------
        // TaskItem → TaskOverviewDto
        // -------------------------------------------------------------------------

        public static TaskOverviewDto ToOverviewDto(this TaskItem task) =>
            new(
                task.Title,
                task.Date
            );

        // -------------------------------------------------------------------------
        // TaskOccurrenceResult → TaskOverviewDto
        // REFACTORED: new overload for recurring-tasks feature
        // -------------------------------------------------------------------------

        public static TaskOverviewDto ToOverviewDto(this TaskOccurrenceResult occurrence) =>
            new(occurrence.Title, occurrence.Date);

        // -------------------------------------------------------------------------
        // List helpers — TaskItem versions (kept for callers that still use TaskItem lists)
        // -------------------------------------------------------------------------

        public static IReadOnlyList<TaskSummaryDto> ToSummaryDtoList(this IEnumerable<TaskItem> tasks) =>
            tasks.Select(t => t.ToSummaryDto())
                 .ToList();

        public static IReadOnlyList<TaskOverviewDto> ToOverviewDtoList(this IEnumerable<TaskItem> tasks) =>
            tasks.Select(t => t.ToOverviewDto())
                 .ToList();

        // -------------------------------------------------------------------------
        // List helpers — TaskOccurrenceResult versions
        // REFACTORED: new overloads for recurring-tasks feature
        // -------------------------------------------------------------------------

        public static IReadOnlyList<TaskSummaryDto> ToSummaryDtoList(this IEnumerable<TaskOccurrenceResult> occurrences) =>
            occurrences.Select(o => o.ToSummaryDto())
                       .ToList();

        public static IReadOnlyList<TaskOverviewDto> ToOverviewDtoList(this IEnumerable<TaskOccurrenceResult> occurrences) =>
            occurrences.Select(o => o.ToOverviewDto())
                       .ToList();
    }
}
