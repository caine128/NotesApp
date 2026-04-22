using FluentValidation;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrenceSubtasks
{
    public sealed class UpdateRecurringTaskOccurrenceSubtasksCommandValidator
        : AbstractValidator<UpdateRecurringTaskOccurrenceSubtasksCommand>
    {
        public UpdateRecurringTaskOccurrenceSubtasksCommandValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEqual(Guid.Empty)
                .WithMessage("SeriesId must be a valid non-empty GUID.");

            RuleFor(x => x.Scope)
                .Must(s => Enum.IsDefined(typeof(RecurringEditScope), s))
                .WithMessage("Scope must be Single, ThisAndFollowing, or All.");

            // TaskItemId, when provided, must be a non-empty GUID.
            When(x => x.TaskItemId.HasValue, () =>
            {
                RuleFor(x => x.TaskItemId!.Value)
                    .NotEqual(Guid.Empty)
                    .WithMessage("TaskItemId must be a valid non-empty GUID when provided.");
            });

            // OccurrenceDate is required for virtual Single and ThisAndFollowing.
            // For materialized Single (TaskItemId provided) the task is identified by TaskItemId, not date.
            // For All scope, OccurrenceDate is not used.
            When(x => x.Scope != RecurringEditScope.All && !x.TaskItemId.HasValue, () =>
            {
                RuleFor(x => x.OccurrenceDate)
                    .NotEqual(default(DateOnly))
                    .WithMessage("OccurrenceDate is required for virtual Single and ThisAndFollowing scopes.");
            });

            RuleForEach(x => x.Subtasks)
                .ChildRules(st =>
                {
                    st.RuleFor(s => s.Text)
                        .NotEmpty()
                        .WithMessage("Subtask text cannot be empty.")
                        .MaximumLength(RecurringTaskSubtask.MaxTextLength)
                        .WithMessage($"Subtask text cannot exceed {RecurringTaskSubtask.MaxTextLength} characters.");

                    st.RuleFor(s => s.Position)
                        .NotEmpty()
                        .WithMessage("Subtask position cannot be empty.")
                        .MaximumLength(RecurringTaskSubtask.MaxPositionLength)
                        .WithMessage($"Subtask position cannot exceed {RecurringTaskSubtask.MaxPositionLength} characters.");
                });
        }
    }
}
