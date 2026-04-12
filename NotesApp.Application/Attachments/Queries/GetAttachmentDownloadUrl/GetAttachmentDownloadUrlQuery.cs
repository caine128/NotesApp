using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.Attachments.Queries.GetAttachmentDownloadUrl
{
    /// <summary>
    /// Query to obtain a pre-signed download URL for a task attachment.
    /// The URL validity period is configured via <c>AttachmentStorage:DownloadUrlValidityMinutes</c>.
    /// </summary>
    public sealed record GetAttachmentDownloadUrlQuery(Guid AttachmentId) : IRequest<Result<string>>;
}
