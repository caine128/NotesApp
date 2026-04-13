using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.DeleteNote
{
    // REFACTORED: converted from positional record to class to support RowVersion binding from request body.
    public sealed class DeleteNoteCommand : IRequest<Result>
    {
        /// <summary>Set from route by the controller.</summary>
        public Guid NoteId { get; set; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
