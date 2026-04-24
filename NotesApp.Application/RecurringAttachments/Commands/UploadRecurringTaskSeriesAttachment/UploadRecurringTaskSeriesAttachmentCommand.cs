using FluentResults;
using MediatR;
using NotesApp.Application.RecurringAttachments.Models;
using System;
using System.IO;

namespace NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskSeriesAttachment
{
    /// <summary>
    /// Uploads a file and attaches it to a recurring task series template.
    /// All occurrences that have not yet overridden their attachment list will inherit this file.
    ///
    /// Validation is handled by <see cref="UploadRecurringTaskSeriesAttachmentCommandValidator"/>.
    /// Business validation (series ownership, content-type whitelist, attachment count limit)
    /// is performed in <see cref="UploadRecurringTaskSeriesAttachmentCommandHandler"/>.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class UploadRecurringTaskSeriesAttachmentCommand
        : IRequest<Result<UploadRecurringAttachmentResultDto>>
    {
        public Guid SeriesId { get; init; }
        public Stream Content { get; init; } = Stream.Null;
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }
}
