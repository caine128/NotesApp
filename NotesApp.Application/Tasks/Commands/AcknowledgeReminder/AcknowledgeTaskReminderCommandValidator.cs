using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.AcknowledgeReminder
{
    /// <summary>
    /// Validator for <see cref="AcknowledgeTaskReminderCommand"/>.
    /// </summary>
    public sealed class AcknowledgeTaskReminderCommandValidator : AbstractValidator<AcknowledgeTaskReminderCommand>
    {
        public AcknowledgeTaskReminderCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty();

            RuleFor(x => x.DeviceId)
                .NotEmpty();

            RuleFor(x => x.AcknowledgedAtUtc)
                .Must(t => t != default)
                .WithMessage("AcknowledgedAtUtc must be a valid UTC timestamp.")
                .Must(t => t.Kind == DateTimeKind.Utc)
                .WithMessage("AcknowledgedAtUtc must be specified as UTC.");
        }
    }
}
