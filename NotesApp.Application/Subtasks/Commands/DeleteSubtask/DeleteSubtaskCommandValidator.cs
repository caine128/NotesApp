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
        }
    }
}
