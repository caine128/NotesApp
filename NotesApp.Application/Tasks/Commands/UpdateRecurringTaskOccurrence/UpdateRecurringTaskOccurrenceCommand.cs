using FluentResults;
using MediatR;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrence
{
    /// <summary>
    /// Updates one or more occurrences of a recurring task according to the specified scope.
    ///
    /// Scope semantics:
    /// - <see cref="RecurringEditScope.Single"/>:
    ///     Updates a single occurrence via a <see cref="Domain.Entities.RecurringTaskException"/>.
    ///     If <see cref="TaskItemId"/> is provided, the materialized TaskItem is also updated.
    ///     If <see cref="TaskItemId"/> is null, only the exception (virtual override) is created/updated.
    /// - <see cref="RecurringEditScope.ThisAndFollowing"/>:
    ///     Terminates the current series at <see cref="OccurrenceDate"/>, soft-deletes all future
    ///     materialized tasks and exceptions, then creates a new series segment with the new template
    ///     fields starting from <see cref="OccurrenceDate"/>.
    /// - <see cref="RecurringEditScope.All"/>:
    ///     Updates the template fields of all active series segments for the recurring root.
    ///     Materialized TaskItems without an individual exception are also updated.
    ///     Note: recurrence pattern changes (RRuleString) require ThisAndFollowing scope.
    /// </summary>
    public sealed class UpdateRecurringTaskOccurrenceCommand : IRequest<Result<TaskDetailDto>>
    {
        // -------------------------------------------------------------------------
        // Scope selector
        // -------------------------------------------------------------------------

        /// <summary>How many occurrences to update.</summary>
        public RecurringEditScope Scope { get; init; }

        // -------------------------------------------------------------------------
        // Occurrence identifier
        // -------------------------------------------------------------------------

        /// <summary>
        /// Id of the materialized TaskItem being updated.
        /// Required for <see cref="RecurringEditScope.Single"/> when the occurrence is materialized.
        /// Null for virtual occurrences (Single scope) or bulk scopes (ThisAndFollowing / All).
        /// The API controller sets this from the route parameter — settable so the controller can override.
        /// </summary>
        public Guid? TaskItemId { get; set; }

        /// <summary>
        /// Id of the RecurringTaskSeries that owns the occurrence.
        /// Required for all scopes.
        /// </summary>
        public Guid SeriesId { get; init; }

        /// <summary>
        /// Canonical (recurrence-engine-generated) occurrence date.
        /// Required for <see cref="RecurringEditScope.Single"/> and
        /// <see cref="RecurringEditScope.ThisAndFollowing"/>.
        /// </summary>
        public DateOnly OccurrenceDate { get; init; }

        /// <summary>
        /// Concurrency token from the last GET, used to detect stale updates on materialized tasks.
        /// Only relevant for <see cref="RecurringEditScope.Single"/> with a materialized occurrence.
        /// </summary>
        public byte[]? RowVersion { get; init; }

        // -------------------------------------------------------------------------
        // Task template fields (used by all scopes)
        // -------------------------------------------------------------------------

        /// <summary>New title. Required for all scopes.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>New description. Null clears the field.</summary>
        public string? Description { get; init; }

        /// <summary>
        /// New display date for this specific occurrence.
        /// Relevant only for Single scope (stored as OverrideDate in the exception).
        /// Ignored for bulk scopes.
        /// </summary>
        public DateOnly? OverrideDate { get; init; }

        /// <summary>New start time. Null clears the field.</summary>
        public TimeOnly? StartTime { get; init; }

        /// <summary>New end time. Null clears the field.</summary>
        public TimeOnly? EndTime { get; init; }

        /// <summary>New location. Null clears the field.</summary>
        public string? Location { get; init; }

        /// <summary>New travel time. Null clears the field.</summary>
        public TimeSpan? TravelTime { get; init; }

        /// <summary>
        /// New category. Null clears the field (uncategorized).
        /// When provided, ownership is validated against the current user.
        /// </summary>
        public Guid? CategoryId { get; init; }

        /// <summary>New priority.</summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;

        /// <summary>New meeting link. Null clears the field.</summary>
        public string? MeetingLink { get; init; }

        /// <summary>
        /// Completion state for this occurrence.
        /// For Single scope — materialized: applied directly to the TaskItem via SetCompleted().
        /// For Single scope — virtual: stored on the RecurringTaskException.IsCompleted.
        /// Ignored for ThisAndFollowing and All scopes (completion is per-occurrence only).
        /// </summary>
        public bool IsCompleted { get; init; }

        // -------------------------------------------------------------------------
        // Reminder fields
        // -------------------------------------------------------------------------

        /// <summary>
        /// Absolute UTC reminder for a single materialized occurrence.
        /// Relevant only for Single scope with a materialized TaskItem.
        /// Null clears the reminder.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }

        /// <summary>
        /// Minutes before StartTime for the series reminder template.
        /// Used by ThisAndFollowing (new series segment) and All (update existing segments).
        /// Null = no reminder.
        /// </summary>
        public int? ReminderOffsetMinutes { get; init; }

        // -------------------------------------------------------------------------
        // Pattern change (ThisAndFollowing only)
        // -------------------------------------------------------------------------

        /// <summary>
        /// New RFC 5545 RRULE string for the new series segment.
        /// Relevant only for <see cref="RecurringEditScope.ThisAndFollowing"/>.
        /// When null, the existing RRuleString is carried forward from the terminated series.
        /// </summary>
        public string? NewRRuleString { get; init; }

        /// <summary>
        /// New exclusive end date for the new series segment.
        /// Relevant only for <see cref="RecurringEditScope.ThisAndFollowing"/>.
        /// Null = no explicit end (never / AfterCount from RRuleString).
        /// </summary>
        public DateOnly? NewEndsBeforeDate { get; init; }

        /// <summary>
        /// New template subtask list for the new series segment.
        /// Relevant only for <see cref="RecurringEditScope.ThisAndFollowing"/>.
        /// Null = carry forward existing template subtasks (not yet implemented in v1 — caller re-sends them).
        /// </summary>
        public IReadOnlyList<TemplateSubtaskDto>? NewTemplateSubtasks { get; init; }
    }
}
