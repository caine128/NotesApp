using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.SetTaskCompletion
{
    /// <summary>
    /// Validates the SetTaskCompletionCommand before it reaches the handler.
    /// We only need to ensure the TaskId is not empty here.
    /// </summary>
    public sealed class SetTaskCompletionCommandValidator
        : AbstractValidator<SetTaskCompletionCommand>
    {
        public SetTaskCompletionCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
