using FluentValidation;
using NotesApp.Domain.Common;
using System;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrence
{
    public sealed class UpdateRecurringTaskOccurrenceCommandValidator
        : AbstractValidator<UpdateRecurringTaskOccurrenceCommand>
    {
        public UpdateRecurringTaskOccurrenceCommandValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEqual(Guid.Empty)
                .WithMessage("SeriesId must be a valid non-empty GUID.");

            RuleFor(x => x.Scope)
                .Must(s => Enum.IsDefined(typeof(RecurringEditScope), s))
                .WithMessage("Scope must be Single, ThisAndFollowing, or All.");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(4000)
                .WithMessage("Description cannot exceed 4000 characters.");

            RuleFor(x => x.Location)
                .MaximumLength(200)
                .WithMessage("Location cannot exceed 200 characters.");

            RuleFor(x => x.MeetingLink)
                .MaximumLength(2048)
                .WithMessage("Meeting link cannot exceed 2048 characters.");

            RuleFor(x => x)
                .Must(x =>
                    !x.StartTime.HasValue ||
                    !x.EndTime.HasValue ||
                    x.EndTime.Value >= x.StartTime.Value)
                .WithMessage("EndTime cannot be earlier than StartTime.");

            RuleFor(x => x.Priority)
                .Must(p => Enum.IsDefined(typeof(TaskPriority), p))
                .WithMessage("Priority must be Low, Normal, or High.");

            // OccurrenceDate is required for Single and ThisAndFollowing.
            When(x => x.Scope != RecurringEditScope.All, () =>
            {
                RuleFor(x => x.OccurrenceDate)
                    .NotEqual(default(DateOnly))
                    .WithMessage("OccurrenceDate is required for Single and ThisAndFollowing scopes.");
            });

            // For a materialized single edit, TaskItemId must be non-empty when provided.
            When(x => x.TaskItemId.HasValue, () =>
            {
                RuleFor(x => x.TaskItemId!.Value)
                    .NotEqual(Guid.Empty)
                    .WithMessage("TaskItemId must be a valid non-empty GUID when provided.");
            });

            // NewRRuleString validation — only relevant for ThisAndFollowing.
            When(x => x.Scope == RecurringEditScope.ThisAndFollowing && x.NewRRuleString != null, () =>
            {
                RuleFor(x => x.NewRRuleString!)
                    .NotEmpty()
                    .WithMessage("NewRRuleString cannot be empty when provided.")
                    .Must(s => s.Contains("FREQ=", StringComparison.OrdinalIgnoreCase))
                    .WithMessage("NewRRuleString must contain a FREQ component (e.g. FREQ=WEEKLY).");
            });

            // ReminderOffsetMinutes must be non-negative when provided.
            RuleFor(x => x.ReminderOffsetMinutes)
                .Must(r => r == null || r.Value >= 0)
                .WithMessage("ReminderOffsetMinutes cannot be negative.");

            // Template subtasks validation (ThisAndFollowing scope).
            When(x => x.NewTemplateSubtasks != null, () =>
            {
                RuleForEach(x => x.NewTemplateSubtasks!)
                    .ChildRules(st =>
                    {
                        st.RuleFor(s => s.Text)
                            .NotEmpty()
                            .WithMessage("Subtask text cannot be empty.");
                        st.RuleFor(s => s.Position)
                            .NotEmpty()
                            .WithMessage("Subtask position cannot be empty.");
                    });
            });
        }
    }
}
