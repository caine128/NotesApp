using System;

namespace NotesApp.Application.RecurringAttachments.Models
{
    /// <summary>
    /// Result returned after a successful recurring task attachment upload.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed record UploadRecurringAttachmentResultDto
    {
        public Guid AttachmentId { get; init; }
        public Guid? SeriesId { get; init; }
        public Guid? ExceptionId { get; init; }
        public int DisplayOrder { get; init; }

        /// <summary>
        /// Pre-signed download URL for the uploaded file.
        /// May be null if URL generation failed transiently after a successful upload.
        /// Use the download-url endpoint to fetch separately when null.
        /// </summary>
        public string? DownloadUrl { get; init; }
    }
}
