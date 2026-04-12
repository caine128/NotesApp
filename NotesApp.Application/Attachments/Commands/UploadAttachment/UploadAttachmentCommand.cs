using FluentResults;
using MediatR;
using NotesApp.Application.Attachments.Models;
using System;
using System.IO;

namespace NotesApp.Application.Attachments.Commands.UploadAttachment
{
    /// <summary>
    /// Command to upload a file and attach it to a task.
    ///
    /// Validation is handled by <see cref="UploadAttachmentCommandValidator"/> via the MediatR pipeline.
    /// Business validation (task ownership, content type whitelist, attachment count limit)
    /// is performed in <see cref="UploadAttachmentCommandHandler"/>.
    /// </summary>
    public sealed class UploadAttachmentCommand : IRequest<Result<UploadAttachmentResultDto>>
    {
        public Guid TaskId { get; init; }
        public Stream Content { get; init; } = Stream.Null;
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }
}
