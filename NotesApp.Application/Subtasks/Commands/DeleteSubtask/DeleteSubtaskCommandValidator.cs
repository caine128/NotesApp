using FluentValidation;

namespace NotesApp.Application.Subtasks.Commands.DeleteSubtask
{
    /// <summary>
    /// FluentValidation validator for <see cref="DeleteSubtaskCommand"/>.
    /// </summary>
    public sealed class DeleteSubtaskCommandValidator : AbstractValidator<DeleteSubtaskCommand>
    {
        public DeleteSubtaskCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");

            RuleFor(x => x.SubtaskId)
                .NotEmpty()
                .WithMessage("SubtaskId is required.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
