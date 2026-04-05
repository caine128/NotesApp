using FluentValidation;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Subtasks.Commands.CreateSubtask
{
    /// <summary>
    /// FluentValidation validator for <see cref="CreateSubtaskCommand"/>.
    /// </summary>
    public sealed class CreateSubtaskCommandValidator : AbstractValidator<CreateSubtaskCommand>
    {
        public CreateSubtaskCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");

            RuleFor(x => x.Text)
                .NotEmpty()
                .WithMessage("Text is required.")
                .MaximumLength(Subtask.MaxTextLength)
                .WithMessage($"Text must be at most {Subtask.MaxTextLength} characters.");

            RuleFor(x => x.Position)
                .NotEmpty()
                .WithMessage("Position is required.")
                .MaximumLength(Subtask.MaxPositionLength)
                .WithMessage($"Position must be at most {Subtask.MaxPositionLength} characters.");
        }
    }
}
