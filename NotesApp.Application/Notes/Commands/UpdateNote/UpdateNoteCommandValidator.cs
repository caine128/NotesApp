using FluentValidation;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.UpdateNote
{
    public sealed class UpdateNoteCommandValidator : AbstractValidator<UpdateNoteCommand>
    {
        public UpdateNoteCommandValidator()
        {
            RuleFor(x => x.NoteId)
                .NotEmpty()
                .WithMessage("Note id is required.");

            RuleFor(x => x.Date)
                .NotEqual(default(DateOnly))
                .WithMessage("Date is required.");

            // CHANGED: Title is now required (content is in blocks)
            RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Note title is required.")
                .MaximumLength(Note.MaxTitleLength)
                .WithMessage("Title cannot exceed 200 characters.");

            

            RuleFor(x => x.Summary)
                .MaximumLength(4000)
                .WithMessage("Summary cannot exceed 4000 characters.");

            RuleFor(x => x.Tags)
                .MaximumLength(1000)
                .WithMessage("Tags cannot exceed 1000 characters.");
        }
    }
}
