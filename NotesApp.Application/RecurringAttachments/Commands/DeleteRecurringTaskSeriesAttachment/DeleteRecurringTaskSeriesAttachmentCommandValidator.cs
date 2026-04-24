using FluentValidation;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskSeriesAttachment
{
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskSeriesAttachmentCommandValidator
        : AbstractValidator<DeleteRecurringTaskSeriesAttachmentCommand>
    {
        public DeleteRecurringTaskSeriesAttachmentCommandValidator()
        {
            RuleFor(x => x.AttachmentId)
                .NotEmpty()
                .WithMessage("AttachmentId is required.");
        }
    }
}
