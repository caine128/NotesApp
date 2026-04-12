using NotesApp.Application.Attachments.Models;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Attachments
{
    /// <summary>
    /// Extension methods for mapping <see cref="Attachment"/> domain entities to DTOs.
    /// </summary>
    public static class AttachmentMappings
    {
        /// <summary>
        /// Maps an <see cref="Attachment"/> to its read model <see cref="AttachmentDto"/>.
        /// </summary>
        public static AttachmentDto ToAttachmentDto(this Attachment attachment) =>
            new(attachment.Id,
                attachment.TaskId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.DisplayOrder,
                attachment.CreatedAtUtc,
                attachment.UpdatedAtUtc);
    }
}
