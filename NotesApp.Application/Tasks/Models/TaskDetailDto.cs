using NotesApp.Application.Attachments.Models;
using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    public sealed record TaskDetailDto(Guid TaskId,
                                       string Title,
                                       string? Description,
                                       DateOnly Date,
                                       TimeOnly? StartTime,
                                       TimeOnly? EndTime,
                                       bool IsCompleted,
                                       string? Location,
                                       TimeSpan? TravelTime,
                                       DateTime CreatedAtUtc,
                                       DateTime UpdatedAtUtc,
                                       DateTime? ReminderAtUtc,
                                       Guid? CategoryId,
                                       TaskPriority Priority, // REFACTORED: added Priority for task priority feature
                                       string? MeetingLink, // REFACTORED: added MeetingLink for meeting-link feature
                                       byte[] RowVersion) // REFACTORED: added RowVersion for web concurrency protection
    {
        // REFACTORED: added subtasks list for subtasks feature
        /// <summary>
        /// Subtasks belonging to this task, sorted by Position (fractional index).
        /// Empty when the task has no subtasks.
        /// </summary>
        public IReadOnlyList<SubtaskDto> Subtasks { get; init; } = [];

        // REFACTORED: added attachments list for task-attachments feature
        /// <summary>
        /// File attachments belonging to this task, sorted by DisplayOrder (upload order).
        /// Empty when the task has no attachments.
        /// Use GET /api/attachments/{id}/download-url to obtain a pre-signed URL on demand.
        /// </summary>
        public IReadOnlyList<AttachmentDto> Attachments { get; init; } = [];

        // REFACTORED: added recurring attachments list for recurring-task-attachments feature
        /// <summary>
        /// Attachments from the recurring series template or from the occurrence's exception override.
        /// Resolution rule: if the occurrence has HasAttachmentOverride=true, these come from the
        /// exception; otherwise they are inherited from the series template.
        /// Always empty for non-recurring tasks.
        /// Use GET /api/recurring-attachments/series/{id}/download-url or
        /// /occurrences/{id}/download-url to obtain a pre-signed URL on demand.
        /// </summary>
        public IReadOnlyList<RecurringAttachmentDto> RecurringAttachments { get; init; } = [];
    }
}
