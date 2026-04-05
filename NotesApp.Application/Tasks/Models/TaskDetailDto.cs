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
                                       TaskPriority Priority) // REFACTORED: added Priority for task priority feature
    {
        // REFACTORED: added subtasks list for subtasks feature
        /// <summary>
        /// Subtasks belonging to this task, sorted by Position (fractional index).
        /// Empty when the task has no subtasks.
        /// </summary>
        public IReadOnlyList<SubtaskDto> Subtasks { get; init; } = [];
    }
}
