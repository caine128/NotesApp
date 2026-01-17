using FluentValidation;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Commands.CreateNote
{
    /// <summary>
    /// FluentValidation validator for CreateNoteCommand.
    /// Performs basic input validation before we hit the domain layer.
    /// </summary>
    public sealed class CreateNoteCommandValidator : AbstractValidator<CreateNoteCommand>
    {
        public CreateNoteCommandValidator()
        {
            // Date must be a valid calendar date (not default)
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");

            // CHANGED: Title is now required (content is in blocks)
            RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Note title is required.")
                .MaximumLength(Note.MaxTitleLength)
                .WithMessage("Title cannot exceed 200 characters.");

        

            RuleFor(x => x.Tags)
                 .MaximumLength(1000)
                 .WithMessage("Tags cannot exceed 1000 characters.");

            RuleFor(x => x.Summary)
               .MaximumLength(4000)
               .WithMessage("Summary cannot exceed 4000 characters.");
        }
    }
}
