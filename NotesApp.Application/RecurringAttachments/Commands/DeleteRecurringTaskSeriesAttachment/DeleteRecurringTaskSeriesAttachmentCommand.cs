using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskSeriesAttachment
{
    /// <summary>
    /// Soft-deletes a recurring task series template attachment.
    /// This affects all occurrences that have not yet overridden their attachment list.
    ///
    /// The corresponding blob in storage is cleaned up later by the background orphan-cleanup worker
    /// (which checks that no other RecurringTaskAttachment shares the same BlobPath first).
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskSeriesAttachmentCommand : IRequest<Result>
    {
        /// <summary>Set from route by the controller.</summary>
        public Guid AttachmentId { get; set; }

        public byte[] RowVersion { get; init; } = [];
    }
}
