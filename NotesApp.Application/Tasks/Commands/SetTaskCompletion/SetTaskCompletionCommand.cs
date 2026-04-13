using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.SetTaskCompletion
{
    /// <summary>
    /// Command to set the completion state of an existing task.
    /// Idempotent: calling it multiple times with the same IsCompleted value
    /// results in the same final state.
    ///
    /// REFACTORED: converted from positional record to class to support RowVersion binding from request body.
    /// </summary>
    public sealed class SetTaskCompletionCommand : IRequest<Result<TaskDetailDto>>
    {
        /// <summary>Set from route by the controller.</summary>
        public Guid TaskId { get; set; }

        public bool IsCompleted { get; init; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
