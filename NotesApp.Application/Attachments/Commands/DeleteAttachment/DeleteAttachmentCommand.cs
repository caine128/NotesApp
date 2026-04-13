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
        /// <summary>Set from route by the controller.</summary>
        public Guid AttachmentId { get; set; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
