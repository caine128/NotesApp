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
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("Task id is required.");

            RuleFor(x => x.Date)
                .Must(d => d != default)
                .WithMessage("Date is required.");

            RuleFor(x => x.Title)
                .NotEmpty()
                .WithMessage("Title is required.")
                .MaximumLength(200)
                .WithMessage("Title cannot exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(4000)
                .WithMessage("Description cannot exceed 4000 characters.");

            RuleFor(x => x.Location)
                .MaximumLength(200)
                .WithMessage("Location cannot exceed 200 characters.");

            RuleFor(x => x)
                .Must(x =>
                    !x.StartTime.HasValue ||
                    !x.EndTime.HasValue ||
                    x.EndTime.Value >= x.StartTime.Value)
                .WithMessage("EndTime cannot be earlier than StartTime.");

            RuleFor(x => x.TravelTime)
                .Must(t => t == null || t.Value >= TimeSpan.Zero)
                .WithMessage("TravelTime cannot be negative.");

            RuleFor(x => x.ReminderAtUtc)
                .Must(r => r == null || r.Value.Kind == DateTimeKind.Utc)
                .WithMessage("Reminder time must be in UTC (DateTimeKind.Utc) if provided.");
        }
    }
}
