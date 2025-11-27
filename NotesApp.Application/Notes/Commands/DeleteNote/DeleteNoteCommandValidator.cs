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
        }
    }
}
