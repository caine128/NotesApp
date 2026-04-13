using FluentResults;
using MediatR;
using NotesApp.Application.Subtasks.Models;
using System;

namespace NotesApp.Application.Subtasks.Commands.UpdateSubtask
{
    /// <summary>
    /// Command to update an existing subtask.
    ///
    /// All payload fields are optional — null means "no change".
    /// The controller sets <see cref="TaskId"/> and <see cref="SubtaskId"/> from the route.
    ///
    /// Position uses the fractional-index format (e.g. "a0", "a1", "a0V").
    /// Callers are responsible for computing a valid fractional-index position
    /// using the same algorithm as the mobile client
    /// (e.g. <c>@rocicorp/fractional-indexing</c>).
    /// </summary>
    public sealed class UpdateSubtaskCommand : IRequest<Result<SubtaskDto>>
    {
        /// <summary>Parent task ID — set from the route by the controller.</summary>
        public Guid TaskId { get; set; }

        /// <summary>Subtask ID — set from the route by the controller.</summary>
        public Guid SubtaskId { get; set; }

        /// <summary>New text. Null = no change. Non-null must be non-empty.</summary>
        public string? Text { get; init; }

        /// <summary>New completion state. Null = no change.</summary>
        public bool? IsCompleted { get; init; }

        /// <summary>New fractional-index position. Null = no change. Non-null must be non-empty.</summary>
        public string? Position { get; init; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
