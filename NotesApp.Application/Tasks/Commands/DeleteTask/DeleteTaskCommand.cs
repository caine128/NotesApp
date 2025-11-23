using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.DeleteTask
{
    /// <summary>
    /// Command to soft-delete a task.
    /// The task will not be physically removed from the database,
    /// but marked as deleted (IsDeleted = true).
    /// </summary>
    public sealed class DeleteTaskCommand : IRequest<Result>
    {
        /// <summary>
        /// The identifier of the task to delete.
        /// </summary>
        public Guid TaskId { get; init; }
    }
}
