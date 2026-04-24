using System;

namespace NotesApp.Application.RecurringAttachments.Models
{
    /// <summary>
    /// Read model for a recurring task attachment.
    ///
    /// Returned as part of <c>TaskDetailDto.RecurringAttachments</c> (both virtual occurrences
    /// and materialized task details). Indicates whether the attachment is a series template
    /// attachment or an exception override via the SeriesId / ExceptionId fields.
    ///
    /// Download URLs are NOT included here — use
    /// GET /api/recurring-attachments/series/{id}/download-url or
    /// GET /api/recurring-attachments/occurrences/{id}/download-url
    /// to obtain a pre-signed URL on demand.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed record RecurringAttachmentDto(
        Guid AttachmentId,
        Guid? SeriesId,
        Guid? ExceptionId,
        string FileName,
        string ContentType,
        long SizeBytes,
        int DisplayOrder,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        byte[] RowVersion);
}
