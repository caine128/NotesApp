using FluentValidation;
using System;

namespace NotesApp.Application.Attachments.Queries.GetAttachmentDownloadUrl
{
    public sealed class GetAttachmentDownloadUrlQueryValidator
        : AbstractValidator<GetAttachmentDownloadUrlQuery>
    {
        public GetAttachmentDownloadUrlQueryValidator()
        {
            RuleFor(x => x.AttachmentId)
                .NotEmpty()
                .WithMessage("AttachmentId is required.");
        }
    }
}
