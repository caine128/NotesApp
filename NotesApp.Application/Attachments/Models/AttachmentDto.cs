using System;

namespace NotesApp.Application.Attachments.Models
{
    /// <summary>
    /// Read model for a task attachment.
    ///
    /// Returned by <c>GetTaskDetailQueryHandler</c> (as part of <c>TaskDetailDto.Attachments</c>)
    /// and by the <c>AttachmentsController</c> list endpoints.
    ///
    /// Download URLs are NOT included here — use GET /api/attachments/{id}/download-url
    /// to obtain a pre-signed URL on demand (same pattern as AssetSyncItemDto / AssetsController).
    /// </summary>
    public sealed record AttachmentDto(
        Guid AttachmentId,
        Guid TaskId,
        string FileName,
        string ContentType,
        long SizeBytes,
        int DisplayOrder,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
