using FluentValidation;
using NotesApp.Domain.Common;
using System;

namespace NotesApp.Application.Tasks.Commands.DeleteRecurringTaskOccurrence
{
    public sealed class DeleteRecurringTaskOccurrenceCommandValidator
        : AbstractValidator<DeleteRecurringTaskOccurrenceCommand>
    {
        public DeleteRecurringTaskOccurrenceCommandValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEqual(Guid.Empty)
                .WithMessage("SeriesId must be a valid non-empty GUID.");

            RuleFor(x => x.Scope)
                .Must(s => Enum.IsDefined(typeof(RecurringDeleteScope), s))
                .WithMessage("Scope must be Single, ThisAndFollowing, or All.");

            // OccurrenceDate is required for Single and ThisAndFollowing.
            When(x => x.Scope != RecurringDeleteScope.All, () =>
            {
                RuleFor(x => x.OccurrenceDate)
                    .NotEqual(default(DateOnly))
                    .WithMessage("OccurrenceDate is required for Single and ThisAndFollowing scopes.");
            });

            // For a materialized single delete, TaskItemId must be non-empty when provided.
            When(x => x.TaskItemId.HasValue, () =>
            {
                RuleFor(x => x.TaskItemId!.Value)
                    .NotEqual(Guid.Empty)
                    .WithMessage("TaskItemId must be a valid non-empty GUID when provided.");
            });
        }
    }
}
