using FluentValidation;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Entities;
using System.IO;

namespace NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskSeriesAttachment
{
    /// <summary>
    /// FluentValidation validator for <see cref="UploadRecurringTaskSeriesAttachmentCommand"/>.
    /// Mirrors <c>UploadAttachmentCommandValidator</c> — input-only checks.
    /// Business validations that require database access remain in the handler.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class UploadRecurringTaskSeriesAttachmentCommandValidator
        : AbstractValidator<UploadRecurringTaskSeriesAttachmentCommand>
    {
        public UploadRecurringTaskSeriesAttachmentCommandValidator()
        {
            RuleFor(x => x.SeriesId)
                .NotEmpty()
                .WithMessage("SeriesId is required.");

            RuleFor(x => x.FileName)
                .NotEmpty()
                .WithMessage("FileName is required.")
                .MaximumLength(RecurringTaskAttachment.MaxFileNameLength)
                .WithMessage($"FileName must be at most {RecurringTaskAttachment.MaxFileNameLength} characters.");

            RuleFor(x => x.ContentType)
                .MaximumLength(RecurringTaskAttachment.MaxContentTypeLength)
                .When(x => !string.IsNullOrEmpty(x.ContentType))
                .WithMessage($"ContentType must be at most {RecurringTaskAttachment.MaxContentTypeLength} characters.");

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
