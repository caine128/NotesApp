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

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
