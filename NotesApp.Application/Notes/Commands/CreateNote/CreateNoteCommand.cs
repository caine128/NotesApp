using FluentResults;
using MediatR;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.CreateNote
{
    /// <summary>
    /// Command to create a new note for the current user and a given date.
    /// 
    /// Invariants are enforced in the Note domain entity:
    /// - UserId must be non-empty (we get it from the current user).
    /// - At least Title or Content must have data.
    /// </summary>
    public sealed class CreateNoteCommand : IRequest<Result<NoteDetailDto>>
    {
        /// <summary>
        /// Calendar day the note belongs to.
        /// </summary>
        public DateOnly Date { get; init; }

        /// <summary>
        /// Optional note title. Can be empty as long as Content has data.
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// Optional note content/body.
        /// </summary>
        public string? Content { get; init; }

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
