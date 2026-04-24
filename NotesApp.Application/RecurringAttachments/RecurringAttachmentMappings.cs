using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.RecurringAttachments
{
    /// <summary>
    /// Extension methods for mapping <see cref="RecurringTaskAttachment"/> domain entities to DTOs.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public static class RecurringAttachmentMappings
    {
        /// <summary>
        /// Maps a <see cref="RecurringTaskAttachment"/> to its read model <see cref="RecurringAttachmentDto"/>.
        /// </summary>
        public static RecurringAttachmentDto ToRecurringAttachmentDto(this RecurringTaskAttachment attachment) =>
            new(attachment.Id,
                attachment.SeriesId,
                attachment.ExceptionId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.DisplayOrder,
                attachment.CreatedAtUtc,
                attachment.UpdatedAtUtc,
                attachment.RowVersion);

        /// <summary>
        /// Maps a <see cref="RecurringTaskAttachment"/> to its sync read model <see cref="RecurringAttachmentSyncItemDto"/>.
        /// </summary>
        public static RecurringAttachmentSyncItemDto ToSyncDto(this RecurringTaskAttachment attachment) =>
            new(attachment.Id,
                attachment.UserId,
                attachment.SeriesId,
                attachment.ExceptionId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.DisplayOrder,
                attachment.CreatedAtUtc,
                attachment.UpdatedAtUtc);
    }
}
