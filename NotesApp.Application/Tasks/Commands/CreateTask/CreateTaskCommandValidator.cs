using FluentValidation;
using NotesApp.Domain.Common;
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

            // CategoryId must be a non-empty GUID when provided.
            RuleFor(x => x.CategoryId)
                .Must(id => id == null || id.Value != Guid.Empty)
                .WithMessage("CategoryId must be a valid non-empty GUID when provided.");

            // Guard against invalid int values sent over the wire (e.g. 0 or 99).
            RuleFor(x => x.Priority) // REFACTORED: added Priority validation for task priority feature
                .Must(p => Enum.IsDefined(typeof(TaskPriority), p))
                .WithMessage("Priority must be Low, Normal, or High.");

            // REFACTORED: added MeetingLink validation for meeting-link feature
            RuleFor(x => x.MeetingLink)
                .MaximumLength(2048)
                .WithMessage("Meeting link cannot exceed 2048 characters.");
        }
    }
}
