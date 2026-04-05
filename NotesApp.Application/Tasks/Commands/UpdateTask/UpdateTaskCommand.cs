using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.UpdateTask
{
    /// <summary>
    /// Command to update an existing task:
    /// - Change title
    /// - Change date
    /// - Change reminder
    ///
    /// The TaskId comes from the route (API) and is also present here
    /// so the handler can load the correct TaskItem from the repository.
    /// </summary>
    public sealed class UpdateTaskCommand : IRequest<Result<TaskDetailDto>>
    {
        /// <summary>
        /// The id of the task to update.
        /// </summary>
        public Guid TaskId { get; set; }

        /// <summary>
        /// New date for the task (required).
        /// </summary>
        public DateOnly Date { get; init; }

        /// <summary>
        /// New title for the task (required, non-empty).
        /// </summary>
        public string Title { get; init; } = string.Empty;

        public string? Description { get; init; }

        public TimeOnly? StartTime { get; init; }

        public TimeOnly? EndTime { get; init; }

        public string? Location { get; init; }

        public TimeSpan? TravelTime { get; init; }

        /// <summary>
        /// Optional new reminder time in UTC. Use null to clear any reminder.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }

        /// <summary>
        /// Optional id of the user-defined category to assign to this task.
        /// Must be a non-empty GUID when provided, and must belong to the current user.
        /// Pass null to clear the category assignment.
        /// </summary>
        public Guid? CategoryId { get; init; }

        /// <summary>
        /// Priority level for this task. Defaults to <see cref="TaskPriority.Normal"/> when not specified.
        /// </summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal; // REFACTORED: added Priority for task priority feature
    }
}
