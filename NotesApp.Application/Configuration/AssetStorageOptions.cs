using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace NotesApp.Application.Configuration
{
    /// <summary>
    /// Configuration options for asset storage operations.
    /// 
    /// These values are used by:
    /// - UploadAssetCommandHandler (upload, download URL generation, cleanup)
    /// - GetSyncChangesQueryHandler (download URL generation for sync)
    /// - UploadAssetCommandValidator (input validation)
    /// 
    /// Bind from configuration section "AssetStorage" in appsettings.json:
    /// {
    ///   "AssetStorage": {
    ///     "ContainerName": "user-assets",
    ///     "DownloadUrlValidityMinutes": 60,
    ///     "MaxFileSizeBytes": 52428800
    ///   }
    /// }
    /// </summary>
    public sealed class AssetStorageOptions
    {
        /// <summary>
        /// Name of the configuration section used to bind these options.
        /// </summary>
        public const string SectionName = "AssetStorage";

        /// <summary>
        /// Default maximum file size (50 MB). Used by validators for static validation.
        /// Runtime value may differ if configured.
        /// </summary>
        public const long DefaultMaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

        /// <summary>
        /// Blob storage container name for user assets.
        /// Default: "user-assets"
        /// </summary>
        [Required]
        [MinLength(3)]
        public string ContainerName { get; set; } = "user-assets";

        /// <summary>
        /// Validity period for pre-signed download URLs, in minutes.
        /// Default: 60 minutes (1 hour)
        /// Must be at least 5 minutes.
        /// </summary>
        [Range(5, 1440)] // 5 minutes to 24 hours
        public int DownloadUrlValidityMinutes { get; set; } = 60;

        /// <summary>
        /// Maximum allowed file size for asset uploads, in bytes.
        /// Default: 50 MB (52,428,800 bytes)
        /// Must be at least 1 KB.
        /// </summary>
        [Range(1024, long.MaxValue)] // At least 1 KB
        public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

        /// <summary>
        /// Gets the download URL validity as a TimeSpan.
        /// </summary>
        public TimeSpan DownloadUrlValidity => TimeSpan.FromMinutes(DownloadUrlValidityMinutes);
    }
}
