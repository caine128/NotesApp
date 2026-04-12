using FluentValidation;
using System;

namespace NotesApp.Application.Attachments.Commands.DeleteAttachment
{
    public sealed class DeleteAttachmentCommandValidator : AbstractValidator<DeleteAttachmentCommand>
    {
        public DeleteAttachmentCommandValidator()
        {
            RuleFor(x => x.AttachmentId)
                .NotEmpty()
                .WithMessage("AttachmentId is required.");
        }
    }
}
