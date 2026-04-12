using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.Attachments.Commands.DeleteAttachment
{
    /// <summary>
    /// Command to soft-delete a task attachment.
    ///
    /// The handler verifies that the attachment belongs to the current user.
    /// The corresponding blob in storage is cleaned up later by the background
    /// orphan-cleanup worker (same pattern as <c>DeleteAsset</c>).
    /// </summary>
    public sealed class DeleteAttachmentCommand : IRequest<Result>
    {
        public Guid AttachmentId { get; init; }
    }
}
