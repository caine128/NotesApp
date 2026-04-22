using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    /// <summary>
    /// Command to create a new task for a given user and date.
    /// In the real app, UserId will come from the authenticated user (JWT),
    /// but for now we keep it as a parameter.
    /// </summary>
    public sealed class CreateTaskCommand : IRequest<Result<TaskDetailDto>>
    {
        public DateOnly Date { get; init; }

        /// <summary>
        /// Required title for the task.
        /// </summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Optional description/details.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Optional local start time.
        /// </summary>
        public TimeOnly? StartTime { get; init; }

        /// <summary>
        /// Optional local end time.
        /// </summary>
        public TimeOnly? EndTime { get; init; }

        /// <summary>
        /// Optional location (address / client name etc.).
        /// </summary>
        public string? Location { get; init; }

        /// <summary>
        /// Optional travel time needed to reach the location.
        /// </summary>
        public TimeSpan? TravelTime { get; init; }

        /// <summary>
        /// Optional reminder time in UTC.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }

        /// <summary>
        /// Optional id of the user-defined category to assign to this task.
        /// Must be a non-empty GUID when provided, and must belong to the current user.
        /// Pass null to create an uncategorized task.
        /// </summary>
        public Guid? CategoryId { get; init; }

        /// <summary>
        /// Priority level for this task. Defaults to <see cref="TaskPriority.Normal"/> when not specified.
        /// </summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal; // REFACTORED: added Priority for task priority feature

        // REFACTORED: added MeetingLink for meeting-link feature
        /// <summary>
        /// Optional join URL or dial-in reference for a meeting associated with this task
        /// (e.g. Zoom, Teams, Google Meet link, or a phone number). Max 2048 characters.
        /// </summary>
        public string? MeetingLink { get; init; }

        // REFACTORED: added recurrence support for recurring-tasks feature

        /// <summary>
        /// When set, the task is created as a recurring series.
        /// The first occurrence is returned in the response (same TaskDetailDto shape).
        /// When null, existing single-task behavior is preserved.
        /// </summary>
        public RecurrenceRuleDto? RecurrenceRule { get; init; }
    }
}
