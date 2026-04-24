using FluentValidation;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskOccurrenceAttachment
{
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskOccurrenceAttachmentCommandValidator
        : AbstractValidator<DeleteRecurringTaskOccurrenceAttachmentCommand>
    {
        public DeleteRecurringTaskOccurrenceAttachmentCommandValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEmpty()
                .WithMessage("SeriesId is required.");

            RuleFor(x => x.OccurrenceDate)
                .NotEqual(default(DateOnly))
                .WithMessage("OccurrenceDate is required.");

            RuleFor(x => x.AttachmentId)
                .NotEmpty()
                .WithMessage("AttachmentId is required.");
        }
    }
}
