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
        public Guid BlockId { get; init; }
    }
}
