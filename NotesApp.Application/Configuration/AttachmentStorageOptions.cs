using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NotesApp.Application.Configuration
{
    /// <summary>
    /// Configuration options for task attachment storage operations.
    ///
    /// These values are used by:
    /// - UploadAttachmentCommandHandler (upload, download URL generation, cleanup, limit enforcement)
    /// - UploadAttachmentCommandValidator (static input validation)
    /// - GetAttachmentDownloadUrlQueryHandler (download URL generation)
    /// - Background orphan-cleanup worker (container name for blob deletion)
    ///
    /// Bind from configuration section "AttachmentStorage" in appsettings.json:
    /// {
    ///   "AttachmentStorage": {
    ///     "ContainerName": "user-attachments",
    ///     "DownloadUrlValidityMinutes": 60,
    ///     "MaxFileSizeBytes": 52428800,
    ///     "MaxAttachmentsPerTask": 5,
    ///     "AllowedContentTypes": ["image/jpeg", "image/png", "application/pdf"]
    ///   }
    /// }
    /// </summary>
    public sealed class AttachmentStorageOptions
    {
        /// <summary>
        /// Name of the configuration section used to bind these options.
        /// </summary>
        public const string SectionName = "AttachmentStorage";

        /// <summary>
        /// Default maximum file size (50 MB). Used by validators for static validation.
        /// Runtime value may differ if configured.
        /// </summary>
        public const long DefaultMaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

        /// <summary>
        /// Blob storage container name for task attachments.
        /// Default: "user-attachments"
        /// </summary>
        [Required]
        [MinLength(3)]
        public string ContainerName { get; set; } = "user-attachments";

        /// <summary>
        /// Validity period for pre-signed download URLs, in minutes.
        /// Default: 60 minutes (1 hour).
        /// Must be at least 5 minutes.
        /// </summary>
        [Range(5, 1440)] // 5 minutes to 24 hours
        public int DownloadUrlValidityMinutes { get; set; } = 60;

        /// <summary>
        /// Maximum allowed file size for attachment uploads, in bytes.
        /// Default: 50 MB (52,428,800 bytes).
        /// Must be at least 1 KB.
        /// </summary>
        [Range(1024, long.MaxValue)] // At least 1 KB
        public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

        /// <summary>
        /// Maximum number of attachments allowed per task.
        /// Default: 5.
        /// </summary>
        [Range(1, 100)]
        public int MaxAttachmentsPerTask { get; set; } = 5;

        /// <summary>
        /// Allowed MIME content types for task attachment uploads.
        /// An empty list means all content types are accepted (permissive mode).
        /// Content type validation against this list is enforced in the handler at runtime.
        /// Default: common image and document formats.
        /// </summary>
        public IReadOnlyList<string> AllowedContentTypes { get; set; } =
        [
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp",
            "application/pdf"
        ];

        /// <summary>
        /// Gets the download URL validity as a <see cref="TimeSpan"/>.
        /// </summary>
        public TimeSpan DownloadUrlValidity => TimeSpan.FromMinutes(DownloadUrlValidityMinutes);
    }
}
