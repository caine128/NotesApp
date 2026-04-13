using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.DeleteBlock
{
    /// <summary>
    /// Command to soft-delete a block.
    /// The block will not be physically removed from the database,
    /// but marked as deleted (IsDeleted = true).
    /// </summary>
    public sealed class DeleteBlockCommand : IRequest<Result>
    {
        /// <summary>
        /// The identifier of the block to delete.
        /// </summary>
        public Guid BlockId { get; set; }

        /// <summary>
        /// When set (non-empty), the handler verifies the block belongs to this note.
        /// Used by the REST endpoint to prevent deleting a block via a different note's URL.
        /// Leave as Guid.Empty when called outside a note-scoped context.
        /// </summary>
        public Guid NoteId { get; set; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
