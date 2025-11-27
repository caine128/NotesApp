using FluentValidation;
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

            RuleFor(x => x)
                .Must(cmd =>
                    !string.IsNullOrWhiteSpace(cmd.Title) ||
                    !string.IsNullOrWhiteSpace(cmd.Content))
                .WithMessage("Note must have at least a title or some content.");

            RuleFor(x => x.Title)
                .MaximumLength(200)
                .WithMessage("Title cannot exceed 200 characters.");

            RuleFor(x => x.Content)
                .MaximumLength(8000)
                .WithMessage("Content cannot exceed 8000 characters.");

            RuleFor(x => x.Summary)
                .MaximumLength(4000)
                .WithMessage("Summary cannot exceed 4000 characters.");

            RuleFor(x => x.Tags)
                .MaximumLength(1000)
                .WithMessage("Tags cannot exceed 1000 characters.");
        }
    }
}
