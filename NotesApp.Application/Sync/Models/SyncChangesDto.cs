using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Top-level DTO returned by the sync pull endpoint.
    /// Contains server timestamp and per-entity-type change buckets.
    /// </summary>
    public sealed record SyncChangesDto
    {
        public DateTime ServerTimestampUtc { get; init; }

        public SyncTasksChangesDto Tasks { get; init; } = new();
        public SyncNotesChangesDto Notes { get; init; } = new();
        public SyncBlocksChangesDto Blocks { get; init; } = new();
        public SyncAssetsChangesDto Assets { get; init; } = new();

        /// <summary>
        /// True when the server had more task changes than were included
        /// in this response (based on MaxItemsPerEntity).
        /// </summary>
        public bool HasMoreTasks { get; init; }
        /// <summary>
        /// True when the server had more note changes than were included
        /// in this response (based on MaxItemsPerEntity).
        /// </summary>
        public bool HasMoreNotes { get; init; }
        /// <summary>
        /// True when the server had more block changes than were included
        /// in this response (based on MaxItemsPerEntity).
        /// </summary>
        public bool HasMoreBlocks { get; init; }
    }

    public sealed record SyncTasksChangesDto
    {
        public IReadOnlyList<TaskSyncItemDto> Created { get; init; } = Array.Empty<TaskSyncItemDto>();
        public IReadOnlyList<TaskSyncItemDto> Updated { get; init; } = Array.Empty<TaskSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    public sealed record SyncNotesChangesDto
    {
        public IReadOnlyList<NoteSyncItemDto> Created { get; init; } = Array.Empty<NoteSyncItemDto>();
        public IReadOnlyList<NoteSyncItemDto> Updated { get; init; } = Array.Empty<NoteSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }
    public sealed record SyncBlocksChangesDto
    {
        public IReadOnlyList<BlockSyncItemDto> Created { get; init; } = Array.Empty<BlockSyncItemDto>();
        public IReadOnlyList<BlockSyncItemDto> Updated { get; init; } = Array.Empty<BlockSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Asset changes for sync pull.
    /// Note: Assets are immutable, so there is no Updated bucket.
    /// </summary>
    public sealed record SyncAssetsChangesDto
    {
        public IReadOnlyList<AssetSyncItemDto> Created { get; init; } = Array.Empty<AssetSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Full task representation used in sync payloads.
    /// Contains all fields required for the client to reconstruct
    /// the latest state plus Version and audit timestamps.
    /// </summary>
    public sealed record TaskSyncItemDto
    {
        public Guid Id { get; init; }

        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }

        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }

        public DateTime? ReminderAtUtc { get; init; }

        public long Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Full note representation used in sync payloads.
    /// </summary>
    public sealed record NoteSyncItemDto
    {
        public Guid Id { get; init; }

        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string? Tags { get; init; }

        public long Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }


    /// <summary>
    /// Full block representation used in sync payloads.
    /// </summary>
    public sealed record BlockSyncItemDto
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

        public long Version { get; init; }

        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Full asset representation used in sync payloads.
    /// Includes a pre-signed download URL for fetching the binary content.
    /// </summary>
    public sealed record AssetSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid BlockId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }

        /// <summary>
        /// Pre-signed URL for downloading the asset.
        /// Valid for a limited time (e.g., 1 hour).
        /// </summary>
        public string? DownloadUrl { get; init; }

        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }


    /// <summary>
    /// Minimal representation for deleted items.
    /// DeletedAtUtc is mapped from the entity's UpdatedAtUtc at deletion time.
    /// </summary>
    public sealed record DeletedSyncItemDto
    {
        public Guid Id { get; init; }
        public DateTime DeletedAtUtc { get; init; }
    }
}
