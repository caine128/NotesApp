using FluentValidation;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Subtasks.Commands.UpdateSubtask
{
    /// <summary>
    /// FluentValidation validator for <see cref="UpdateSubtaskCommand"/>.
    /// </summary>
    public sealed class UpdateSubtaskCommandValidator : AbstractValidator<UpdateSubtaskCommand>
    {
        public UpdateSubtaskCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");

            RuleFor(x => x.SubtaskId)
                .NotEmpty()
                .WithMessage("SubtaskId is required.");

            // When Text is provided it must not be empty/whitespace.
            When(x => x.Text is not null, () =>
            {
                RuleFor(x => x.Text)
                    .NotEmpty()
                    .WithMessage("Text must not be empty when provided.")
                    .MaximumLength(Subtask.MaxTextLength)
                    .WithMessage($"Text must be at most {Subtask.MaxTextLength} characters.");
            });

            // When Position is provided it must not be empty/whitespace.
            When(x => x.Position is not null, () =>
            {
                RuleFor(x => x.Position)
                    .NotEmpty()
                    .WithMessage("Position must not be empty when provided.")
                    .MaximumLength(Subtask.MaxPositionLength)
                    .WithMessage($"Position must be at most {Subtask.MaxPositionLength} characters.");
            });

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
