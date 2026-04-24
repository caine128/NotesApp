using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.RecurringAttachments.Commands.DeleteRecurringTaskOccurrenceAttachment
{
    /// <summary>
    /// Deletes an attachment from a specific recurring task occurrence.
    ///
    /// If the attachment is a series template attachment, the occurrence is promoted to a
    /// <see cref="Domain.Entities.RecurringTaskException"/> with an independent attachment list
    /// (all series template attachments minus the deleted one are copied as exception attachments).
    ///
    /// If the attachment is already an exception-specific attachment, it is simply soft-deleted.
    ///
    /// The blob is only cleaned up by the background orphan-cleanup worker when no other
    /// non-deleted RecurringTaskAttachment references the same BlobPath.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class DeleteRecurringTaskOccurrenceAttachmentCommand : IRequest<Result>
    {
        /// <summary>Set from route by the controller.</summary>
        public Guid SeriesId { get; set; }

        /// <summary>Set from route by the controller.</summary>
        public DateOnly OccurrenceDate { get; set; }

        /// <summary>Set from route by the controller.</summary>
        public Guid AttachmentId { get; set; }

        public byte[] RowVersion { get; init; } = [];
    }
}
