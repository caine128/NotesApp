using FluentResults;
using MediatR;
using NotesApp.Application.RecurringAttachments.Models;
using System;
using System.IO;

namespace NotesApp.Application.RecurringAttachments.Commands.UploadRecurringTaskOccurrenceAttachment
{
    /// <summary>
    /// Uploads a file to a specific recurring task occurrence.
    ///
    /// If the occurrence has no prior attachment override, it is promoted to a
    /// <see cref="Domain.Entities.RecurringTaskException"/> and all current series template
    /// attachments are copied as exception-scoped rows before the new attachment is added.
    ///
    /// Validation is handled by <see cref="UploadRecurringTaskOccurrenceAttachmentCommandValidator"/>.
    /// Business validation is performed in the handler.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class UploadRecurringTaskOccurrenceAttachmentCommand
        : IRequest<Result<UploadRecurringAttachmentResultDto>>
    {
        public Guid SeriesId { get; init; }
        public DateOnly OccurrenceDate { get; init; }
        public Stream Content { get; init; } = Stream.Null;
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }
}
