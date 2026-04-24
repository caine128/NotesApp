using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.RecurringAttachments.Queries.GetRecurringAttachmentDownloadUrl
{
    /// <summary>
    /// Returns a pre-signed download URL for an existing recurring task attachment.
    /// Works for both series template attachments and exception attachment overrides.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed record GetRecurringAttachmentDownloadUrlQuery(Guid AttachmentId)
        : IRequest<Result<string>>;
}
