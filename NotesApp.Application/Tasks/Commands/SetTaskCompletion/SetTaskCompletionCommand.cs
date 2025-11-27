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
    /// </summary>
    public sealed record SetTaskCompletionCommand(
        Guid TaskId,
        bool IsCompleted
    ) : IRequest<Result<TaskDetailDto>>;
}
