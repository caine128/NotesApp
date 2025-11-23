using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.UpdateTask
{
    /// <summary>
    /// FluentValidation validator for UpdateTaskCommand.
    /// Guards basic input rules before hitting the domain layer.
    /// </summary>
    public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
    {
        public UpdateTaskCommandValidator()
        {
            // TaskId is required
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("Task id is required.");

            // Date cannot be the default value
            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");

            // Title must not be empty (domain enforces this as well,
            // but we give the user earlier feedback here)
            RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Title is required.")
                .MaximumLength(200)
                .WithMessage("Title cannot exceed 200 characters.");

            // Optional: basic sanity check on ReminderAtUtc
            RuleFor(x => x.ReminderAtUtc)
                .Must(r => r == null || r.Value.Kind == DateTimeKind.Utc)
                .WithMessage("Reminder time must be in UTC (DateTimeKind.Utc) if provided.");
        }
    }
}
