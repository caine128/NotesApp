using FluentResults;
using MediatR;
using NotesApp.Application.Blocks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.UpdateBlock
{
    /// <summary>
    /// Command to update an existing block.
    /// 
    /// Blocks can update:
    /// - Position: to reorder within parent
    /// - TextContent: for text blocks only
    /// 
    /// Note: Block type cannot be changed after creation.
    /// Asset metadata is immutable - to change an asset, delete and recreate the block.
    /// </summary>
    public sealed class UpdateBlockCommand : IRequest<Result<BlockDetailDto>>
    {
        /// <summary>
        /// The id of the block to update.
        /// </summary>
        public Guid BlockId { get; set; }

        /// <summary>
        /// When set (non-empty), the handler verifies the block belongs to this note.
        /// Used by the REST endpoint to prevent updating a block via a different note's URL.
        /// Leave as Guid.Empty when called outside a note-scoped context.
        /// </summary>
        public Guid NoteId { get; set; }

        /// <summary>
        /// New position (fractional index). Null means no change.
        /// </summary>
        public string? Position { get; init; }

        /// <summary>
        /// New text content. Null means no change (for text blocks).
        /// For text blocks, empty string is valid (clears content).
        /// </summary>
        public string? TextContent { get; init; }
    }
}
