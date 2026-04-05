using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.Subtasks.Commands.DeleteSubtask
{
    /// <summary>
    /// Command to soft-delete a subtask.
    /// The subtask will not be physically removed from the database,
    /// but marked as deleted (IsDeleted = true) and surfaced to other
    /// clients on the next sync pull.
    /// </summary>
    public sealed class DeleteSubtaskCommand : IRequest<Result>
    {
        /// <summary>Parent task ID — set from the route by the controller.</summary>
        public Guid TaskId { get; set; }

        /// <summary>Subtask ID — set from the route by the controller.</summary>
        public Guid SubtaskId { get; set; }
    }
}
