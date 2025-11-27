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
            RuleFor(x => x.Date)
                .NotEqual(default(DateOnly))
                .WithMessage("Date is required.");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Task title is required.")
                .MaximumLength(200).WithMessage("Task title is too long.");

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
        }
    }
}
