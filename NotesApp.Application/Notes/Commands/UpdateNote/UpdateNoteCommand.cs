using FluentResults;
using MediatR;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.UpdateNote
{
    public sealed class UpdateNoteCommand : IRequest<Result<NoteDetailDto>>
    {
        public Guid NoteId { get; set; }

        public DateOnly Date { get; init; }

        public string? Title { get; init; }


        /// <summary>
        /// Optional user-provided summary. AI may override/update later.
        /// </summary>
        public string? Summary { get; init; }

        /// <summary>
        /// Optional user-provided tags (comma or space separated).
        /// </summary>
        public string? Tags { get; init; }
    }
}
