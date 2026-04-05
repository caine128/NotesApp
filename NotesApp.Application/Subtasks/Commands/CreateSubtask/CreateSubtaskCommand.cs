using FluentResults;
using MediatR;
using NotesApp.Application.Subtasks.Models;
using System;

namespace NotesApp.Application.Subtasks.Commands.CreateSubtask
{
    /// <summary>
    /// Command to create a new subtask within an existing task.
    ///
    /// Position uses the fractional-index format (e.g. "a0", "a1", "a0V").
    /// Callers are responsible for computing a valid fractional-index position
    /// using the same algorithm as the mobile client
    /// (e.g. <c>@rocicorp/fractional-indexing</c>).
    /// </summary>
    public sealed class CreateSubtaskCommand : IRequest<Result<SubtaskDto>>
    {
        /// <summary>Parent task ID — set from the route by the controller.</summary>
        public Guid TaskId { get; set; }

        /// <summary>Text / title of the new subtask. Required, max 500 characters.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>
        /// Fractional-index position for ordering within the parent task.
        /// Required, max 100 characters.
        /// </summary>
        public string Position { get; init; } = string.Empty;
    }
}
