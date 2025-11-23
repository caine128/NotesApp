using FluentResults;
using MediatR;
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
    public sealed class UpdateTaskCommand : IRequest<Result<TaskDto>>
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

        /// <summary>
        /// Optional new reminder time in UTC. Use null to clear any reminder.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }
    }
}
