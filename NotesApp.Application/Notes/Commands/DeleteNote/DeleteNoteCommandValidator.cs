using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.DeleteNote
{
    public sealed class DeleteNoteCommandValidator : AbstractValidator<DeleteNoteCommand>
    {
        public DeleteNoteCommandValidator()
        {
            RuleFor(x => x.NoteId)
                .NotEmpty()
                .WithMessage("Note id is required.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
