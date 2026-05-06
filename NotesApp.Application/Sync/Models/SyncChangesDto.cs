using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Sync.Models
{
    // REFACTORED: this file used to contain SyncChangesDto + per-family bucket DTOs for the
    // legacy timestamp-pull endpoint. Both have been removed in the sequence-based pull cutover.
    // Only the per-family ITEM DTOs remain — they are still consumed by SyncChangeWriter and the
    // GetSyncSnapshotQueryHandler via SyncMappings.ToSyncDto() and serialized into SyncPullItemDto.Payload
    // and SyncSnapshotItemDto.Payload.

    /// <summary>
    /// Full task representation used in sync payloads.
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
        public Guid? CategoryId { get; init; }
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;
        public string? MeetingLink { get; init; }
        public Guid? RecurringSeriesId { get; init; }
        public DateOnly? CanonicalOccurrenceDate { get; init; }
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>Full note representation used in sync payloads.</summary>
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

    /// <summary>Full block representation used in sync payloads.</summary>
    public sealed record BlockSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid ParentId { get; init; }
        public BlockParentType ParentType { get; init; }
        public BlockType Type { get; init; }
        public string Position { get; init; } = string.Empty;
        public string? TextContent { get; init; }
        public Guid? AssetId { get; init; }
        public string? AssetClientId { get; init; }
        public string? AssetFileName { get; init; }
        public string? AssetContentType { get; init; }
        public long? AssetSizeBytes { get; init; }
        public UploadStatus UploadStatus { get; init; }
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Full asset representation used in sync payloads.
    /// Download URLs are not included; use GET /api/assets/{id}/download-url on demand.
    /// </summary>
    public sealed record AssetSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid BlockId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>Full category representation used in sync payloads.</summary>
    public sealed record CategorySyncItemDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>Full subtask representation used in sync payloads.</summary>
    public sealed record SubtaskSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid TaskId { get; init; }
        public string Text { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public string Position { get; init; } = string.Empty;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Full attachment representation used in sync pull payloads.
    /// Download URLs are not included; use GET /api/attachments/{id}/download-url on demand.
    /// </summary>
    public sealed record AttachmentSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid TaskId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public int DisplayOrder { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>Sync representation of a RecurringTaskRoot (the stable identity anchor).</summary>
    public sealed record RecurringRootSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Sync representation of a RecurringTaskSeries segment.
    /// RRuleString is sent verbatim so clients can parse it with their own iCal library.
    /// </summary>
    public sealed record RecurringSeriesSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid RootId { get; init; }
        public string RRuleString { get; init; } = string.Empty;
        public DateOnly StartsOnDate { get; init; }
        public DateOnly? EndsBeforeDate { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public Guid? CategoryId { get; init; }
        public TaskPriority Priority { get; init; }
        public string? MeetingLink { get; init; }
        public int? ReminderOffsetMinutes { get; init; }
        public DateOnly MaterializedUpToDate { get; init; }
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Sync representation for a RecurringTaskSubtask row. Covers both series template subtasks
    /// (SeriesId set) and exception overrides (ExceptionId set).
    /// </summary>
    public sealed record RecurringSubtaskSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid? SeriesId { get; init; }
        public Guid? ExceptionId { get; init; }
        public string Text { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public string Position { get; init; } = string.Empty;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>Full exception representation used in sync pull payloads.</summary>
    public sealed record RecurringExceptionSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid SeriesId { get; init; }
        public DateOnly OccurrenceDate { get; init; }
        public bool IsDeletion { get; init; }
        public string? OverrideTitle { get; init; }
        public string? OverrideDescription { get; init; }
        public DateOnly? OverrideDate { get; init; }
        public TimeOnly? OverrideStartTime { get; init; }
        public TimeOnly? OverrideEndTime { get; init; }
        public string? OverrideLocation { get; init; }
        public TimeSpan? OverrideTravelTime { get; init; }
        public Guid? OverrideCategoryId { get; init; }
        public TaskPriority? OverridePriority { get; init; }
        public string? OverrideMeetingLink { get; init; }
        public DateTime? OverrideReminderAtUtc { get; init; }
        public bool IsCompleted { get; init; }
        public Guid? MaterializedTaskItemId { get; init; }
        public IReadOnlyList<RecurringSubtaskSyncItemDto> Subtasks { get; init; } = Array.Empty<RecurringSubtaskSyncItemDto>();
        public bool HasAttachmentOverride { get; init; }
        public IReadOnlyList<RecurringAttachmentSyncItemDto> Attachments { get; init; } = Array.Empty<RecurringAttachmentSyncItemDto>();
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    /// <summary>
    /// Full recurring task attachment representation used in sync pull payloads.
    /// Download URLs are NOT included; use GET on demand.
    /// </summary>
    public sealed record RecurringAttachmentSyncItemDto(
        Guid Id,
        Guid UserId,
        Guid? SeriesId,
        Guid? ExceptionId,
        string FileName,
        string ContentType,
        long SizeBytes,
        int DisplayOrder,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
