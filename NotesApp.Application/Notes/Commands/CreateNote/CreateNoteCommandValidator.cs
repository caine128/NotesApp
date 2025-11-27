using FluentValidation;
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

            // At least Title or Content must be non-empty.
            // This mirrors the invariant in Note.Create, but gives earlier feedback.
            RuleFor(x => x)
                .Must(x =>
                    !string.IsNullOrWhiteSpace(x.Title) ||
                    !string.IsNullOrWhiteSpace(x.Content))
                .WithMessage("Note must have at least a title or some content.");

            // Optional: keep Title length reasonable
            RuleFor(x => x.Title)
                .MaximumLength(200)
                .WithMessage("Title cannot exceed 200 characters.");

            // Optional: keep Content size sane (tune as you like)
            RuleFor(x => x.Content)
                .MaximumLength(4000)
                .WithMessage("Content cannot exceed 4000 characters.");

            RuleFor(x => x.Tags)
                 .MaximumLength(1000)
                 .WithMessage("Tags cannot exceed 1000 characters.");

            RuleFor(x => x.Summary)
               .MaximumLength(4000)
               .WithMessage("Summary cannot exceed 4000 characters.");
        }
    }
}
