using FluentValidation;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Entities;
using System.IO;

namespace NotesApp.Application.Attachments.Commands.UploadAttachment
{
    /// <summary>
    /// FluentValidation validator for <see cref="UploadAttachmentCommand"/>.
    ///
    /// Validates input fields that can be checked without database access:
    /// - TaskId: required
    /// - FileName: required, max length
    /// - ContentType: max length (optional field)
    /// - SizeBytes: positive, within static max limit
    /// - Content: not null stream
    ///
    /// Business validations that require database access remain in the handler:
    /// - Task exists and belongs to the current user
    /// - Content type is in the AllowedContentTypes list (runtime-configured value)
    /// - Attachment count is below MaxAttachmentsPerTask (runtime-configured value)
    ///
    /// Note: Uses AttachmentStorageOptions.DefaultMaxFileSizeBytes for static validation.
    /// The handler may use a different configured value at runtime.
    /// </summary>
    public sealed class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
    {
        public UploadAttachmentCommandValidator()
        {
            RuleFor(x => x.TaskId)
                .NotEmpty()
                .WithMessage("TaskId is required.");

            RuleFor(x => x.FileName)
                .NotEmpty()
                .WithMessage("FileName is required.")
                .MaximumLength(Attachment.MaxFileNameLength)
                .WithMessage($"FileName must be at most {Attachment.MaxFileNameLength} characters.");

            RuleFor(x => x.ContentType)
                .MaximumLength(Attachment.MaxContentTypeLength)
                .When(x => !string.IsNullOrEmpty(x.ContentType))
                .WithMessage($"ContentType must be at most {Attachment.MaxContentTypeLength} characters.");

            RuleFor(x => x.SizeBytes)
                .GreaterThan(0)
                .WithMessage("File size must be positive.")
                .LessThanOrEqualTo(AttachmentStorageOptions.DefaultMaxFileSizeBytes)
                .WithMessage($"File size exceeds the maximum allowed size of {AttachmentStorageOptions.DefaultMaxFileSizeBytes / (1024 * 1024)} MB.");

            RuleFor(x => x.Content)
                .NotNull()
                .WithMessage("Content stream is required.")
                .Must(stream => stream != Stream.Null)
                .WithMessage("Content stream cannot be empty.");
        }
    }
}
