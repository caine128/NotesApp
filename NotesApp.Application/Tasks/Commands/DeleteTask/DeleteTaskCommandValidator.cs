using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.DeleteTask
{
    /// <summary>
    /// Validates the DeleteTaskCommand before it reaches the handler.
    /// </summary>
    public sealed class DeleteTaskCommandValidator
        : AbstractValidator<DeleteTaskCommand>
    {
        public DeleteTaskCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");
        }
    }
}
