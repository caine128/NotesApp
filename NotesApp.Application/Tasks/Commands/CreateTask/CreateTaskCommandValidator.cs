using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
    {
        public CreateTaskCommandValidator()
        {

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Task title is required.")
                .MaximumLength(200).WithMessage("Task title is too long.");
        }
    }
}
