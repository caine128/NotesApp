using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Models
{
    /// <summary>
    /// Full block representation returned from direct API operations (create, update, get).
    /// Contains all block properties including asset metadata and version info.
    /// </summary>
    public sealed record BlockDetailDto
    {
        public Guid Id { get; init; }
        public Guid ParentId { get; init; }
        public BlockParentType ParentType { get; init; }
        public BlockType Type { get; init; }
        public string Position { get; init; } = string.Empty;

        /// <summary>
        /// Text content for text-based blocks. Null for asset blocks.
        /// </summary>
        public string? TextContent { get; init; }

        /// <summary>
        /// Asset ID for asset blocks. Null for text blocks or pending uploads.
        /// </summary>
        public Guid? AssetId { get; init; }

        /// <summary>
        /// Client-generated identifier for tracking asset upload.
        /// </summary>
        public string? AssetClientId { get; init; }

        /// <summary>
        /// Original filename for asset blocks.
        /// </summary>
        public string? AssetFileName { get; init; }

        /// <summary>
        /// MIME type for asset blocks.
        /// </summary>
        public string? AssetContentType { get; init; }

        /// <summary>
        /// File size in bytes for asset blocks.
        /// </summary>
        public long? AssetSizeBytes { get; init; }

        /// <summary>
        /// Upload status for asset blocks.
        /// </summary>
        public UploadStatus UploadStatus { get; init; }

        /// <summary>
        /// Version number for optimistic concurrency.
        /// </summary>
        public long Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }
}
