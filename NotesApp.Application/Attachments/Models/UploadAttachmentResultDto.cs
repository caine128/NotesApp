using System;

namespace NotesApp.Application.Attachments.Models
{
    /// <summary>
    /// Result returned by <c>UploadAttachmentCommandHandler</c> after a successful upload.
    /// </summary>
    public sealed record UploadAttachmentResultDto
    {
        public Guid AttachmentId { get; init; }
        public Guid TaskId { get; init; }
        public int DisplayOrder { get; init; }

        /// <summary>
        /// Pre-signed download URL for the uploaded file.
        /// May be null if URL generation failed transiently after a successful upload.
        /// Use GET /api/attachments/{id}/download-url to fetch separately when null.
        /// </summary>
        public string? DownloadUrl { get; init; }
    }
}
