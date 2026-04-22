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
        // REFACTORED: added category changes bucket
        public SyncCategoriesChangesDto Categories { get; init; } = new();
        // REFACTORED: added subtask changes bucket for subtasks feature
        public SyncSubtasksChangesDto Subtasks { get; init; } = new();

        // REFACTORED: added attachment changes bucket for task-attachments feature
        /// <summary>
        /// Task attachment changes (created/deleted).
        /// No Updated bucket — attachments are immutable after creation.
        /// Download URLs are not included; use GET /api/attachments/{id}/download-url on demand.
        /// </summary>
        public SyncAttachmentsChangesDto Attachments { get; init; } = new();

        // REFACTORED: added recurring-task change buckets for recurring-tasks feature
        public SyncRecurringRootsChangesDto RecurringRoots { get; init; } = new();
        public SyncRecurringSeriesChangesDto RecurringSeries { get; init; } = new();
        public SyncRecurringSeriesSubtasksChangesDto RecurringSeriesSubtasks { get; init; } = new();
        public SyncRecurringExceptionsChangesDto RecurringExceptions { get; init; } = new();

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
        /// <summary>
        /// True when the server had more category changes than were included
        /// in this response (based on DefaultPullMaxCategories).
        /// </summary>
        // REFACTORED: added HasMoreCategories pagination flag
        public bool HasMoreCategories { get; init; }
        // REFACTORED: added HasMoreSubtasks pagination flag for subtasks feature
        /// <summary>
        /// True when the server had more subtask changes than were included
        /// in this response (based on MaxItemsPerEntity).
        /// </summary>
        public bool HasMoreSubtasks { get; init; }

        // REFACTORED: added recurring-task pagination flags for recurring-tasks feature
        /// <summary>True when the server had more recurring root changes than were included in this response.</summary>
        public bool HasMoreRecurringRoots { get; init; }
        /// <summary>True when the server had more recurring series changes than were included in this response.</summary>
        public bool HasMoreRecurringSeries { get; init; }
        /// <summary>True when the server had more recurring series subtask changes than were included in this response.</summary>
        public bool HasMoreRecurringSeriesSubtasks { get; init; }
        /// <summary>True when the server had more recurring exception changes than were included in this response.</summary>
        public bool HasMoreRecurringExceptions { get; init; }
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

        // REFACTORED: added CategoryId for task categories feature
        /// <summary>
        /// Optional category this task belongs to. Null when uncategorised.
        /// </summary>
        public Guid? CategoryId { get; init; }

        // REFACTORED: added Priority for task priority feature
        /// <summary>
        /// Priority level of the task. Normal when not explicitly set.
        /// </summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;

        // REFACTORED: added MeetingLink for meeting-link feature
        /// <summary>
        /// Optional join URL or dial-in reference for a meeting. Null when not set.
        /// </summary>
        public string? MeetingLink { get; init; }

        // REFACTORED: added recurring-task fields for recurring-tasks feature
        /// <summary>FK to the recurring series this task belongs to. Null for non-recurring tasks.</summary>
        public Guid? RecurringSeriesId { get; init; }

        /// <summary>
        /// The recurrence-engine-generated canonical occurrence date.
        /// Null for non-recurring tasks; matches TaskItem.CanonicalOccurrenceDate.
        /// </summary>
        public DateOnly? CanonicalOccurrenceDate { get; init; }

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
    /// Download URLs are not included here; use GET /api/assets/{id}/download-url to obtain one on demand.
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


    /// <summary>
    /// Minimal representation for deleted items.
    /// DeletedAtUtc is mapped from the entity's UpdatedAtUtc at deletion time.
    /// </summary>
    public sealed record DeletedSyncItemDto
    {
        public Guid Id { get; init; }
        public DateTime DeletedAtUtc { get; init; }
    }

    // REFACTORED: added category sync DTOs for task categories feature

    /// <summary>
    /// Category changes bucket returned by the sync pull endpoint.
    /// Mirrors the structure of other entity change buckets (Tasks, Notes, Blocks).
    /// </summary>
    public sealed record SyncCategoriesChangesDto
    {
        public IReadOnlyList<CategorySyncItemDto> Created { get; init; } = Array.Empty<CategorySyncItemDto>();
        public IReadOnlyList<CategorySyncItemDto> Updated { get; init; } = Array.Empty<CategorySyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Full category representation used in sync payloads.
    /// Includes Version so clients can detect concurrent renames and
    /// raise a VersionMismatch conflict on the next push if needed.
    /// </summary>
    public sealed record CategorySyncItemDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    // REFACTORED: added subtask sync DTOs for subtasks feature

    /// <summary>
    /// Subtask changes bucket returned by the sync pull endpoint.
    /// Mirrors the structure of other entity change buckets (Tasks, Notes, Blocks, Categories).
    /// </summary>
    public sealed record SyncSubtasksChangesDto
    {
        public IReadOnlyList<SubtaskSyncItemDto> Created { get; init; } = Array.Empty<SubtaskSyncItemDto>();
        public IReadOnlyList<SubtaskSyncItemDto> Updated { get; init; } = Array.Empty<SubtaskSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Full subtask representation used in sync payloads.
    /// Includes Version so clients can detect concurrent edits and
    /// raise a VersionMismatch conflict on the next push if needed.
    /// </summary>
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

    // REFACTORED: added attachment sync DTOs for task-attachments feature

    /// <summary>
    /// Attachment changes bucket returned by the sync pull endpoint.
    /// No Updated bucket — task attachments are immutable after creation.
    /// </summary>
    public sealed record SyncAttachmentsChangesDto
    {
        public IReadOnlyList<AttachmentSyncItemDto> Created { get; init; } = Array.Empty<AttachmentSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Full attachment representation used in sync pull payloads.
    /// Download URLs are NOT included; use GET /api/attachments/{id}/download-url to obtain one on demand.
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

    // REFACTORED: added recurring-task sync DTOs for recurring-tasks feature

    public sealed record SyncRecurringRootsChangesDto
    {
        public IReadOnlyList<RecurringRootSyncItemDto> Created { get; init; } = Array.Empty<RecurringRootSyncItemDto>();
        public IReadOnlyList<RecurringRootSyncItemDto> Updated { get; init; } = Array.Empty<RecurringRootSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>Sync representation of a RecurringTaskRoot (the stable identity anchor across series segments).</summary>
    public sealed record RecurringRootSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    public sealed record SyncRecurringSeriesChangesDto
    {
        public IReadOnlyList<RecurringSeriesSyncItemDto> Created { get; init; } = Array.Empty<RecurringSeriesSyncItemDto>();
        public IReadOnlyList<RecurringSeriesSyncItemDto> Updated { get; init; } = Array.Empty<RecurringSeriesSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Sync representation of a RecurringTaskSeries segment.
    /// RRuleString is sent verbatim so clients can parse it with their own iCal library (e.g. RRule.js).
    /// </summary>
    public sealed record RecurringSeriesSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid RootId { get; init; }
        /// <summary>RFC 5545 RRULE body (no DTSTART/UNTIL). Clients parse with their own iCal library.</summary>
        public string RRuleString { get; init; } = string.Empty;
        public DateOnly StartsOnDate { get; init; }
        public DateOnly? EndsBeforeDate { get; init; }
        // Template task fields
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

    public sealed record SyncRecurringSeriesSubtasksChangesDto
    {
        public IReadOnlyList<RecurringSubtaskSyncItemDto> Created { get; init; } = Array.Empty<RecurringSubtaskSyncItemDto>();
        public IReadOnlyList<RecurringSubtaskSyncItemDto> Updated { get; init; } = Array.Empty<RecurringSubtaskSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Sync representation for a RecurringTaskSubtask row.
    /// Covers both series template subtasks (SeriesId set) and exception subtask overrides (ExceptionId set).
    /// Clients route based on which FK is non-null.
    /// Also inlined inside RecurringExceptionSyncItemDto.Subtasks for convenience.
    /// </summary>
    public sealed record RecurringSubtaskSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        /// <summary>Set when this row is a series template subtask. Null for exception overrides.</summary>
        public Guid? SeriesId { get; init; }
        /// <summary>Set when this row is an exception subtask override. Null for series template subtasks.</summary>
        public Guid? ExceptionId { get; init; }
        public string Text { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public string Position { get; init; } = string.Empty;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    public sealed record SyncRecurringExceptionsChangesDto
    {
        public IReadOnlyList<RecurringExceptionSyncItemDto> Created { get; init; } = Array.Empty<RecurringExceptionSyncItemDto>();
        public IReadOnlyList<RecurringExceptionSyncItemDto> Updated { get; init; } = Array.Empty<RecurringExceptionSyncItemDto>();
        public IReadOnlyList<DeletedSyncItemDto> Deleted { get; init; } = Array.Empty<DeletedSyncItemDto>();
    }

    /// <summary>
    /// Full exception representation used in sync pull payloads.
    /// Subtasks: empty list = occurrence inherits the series template subtasks.
    /// Non-empty list = complete override subtask list for this occurrence.
    /// </summary>
    public sealed record RecurringExceptionSyncItemDto
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public Guid SeriesId { get; init; }
        public DateOnly OccurrenceDate { get; init; }
        /// <summary>When true, this occurrence is suppressed. All override fields are null.</summary>
        public bool IsDeletion { get; init; }
        // Override fields — all nullable; null means "inherit from series template"
        public string? OverrideTitle { get; init; }
        public string? OverrideDescription { get; init; }
        /// <summary>Moved display date for this occurrence. Null = occurrence stays on OccurrenceDate.</summary>
        public DateOnly? OverrideDate { get; init; }
        public TimeOnly? OverrideStartTime { get; init; }
        public TimeOnly? OverrideEndTime { get; init; }
        public string? OverrideLocation { get; init; }
        public TimeSpan? OverrideTravelTime { get; init; }
        public Guid? OverrideCategoryId { get; init; }
        public TaskPriority? OverridePriority { get; init; }
        public string? OverrideMeetingLink { get; init; }
        public DateTime? OverrideReminderAtUtc { get; init; }
        /// <summary>
        /// Completion state for this specific occurrence.
        /// Stored on the exception (not inherited from series template — the series has no completion state).
        /// False = not completed (default); true = completed.
        /// Ignored when IsDeletion = true.
        /// </summary>
        public bool IsCompleted { get; init; }
        /// <summary>FK to the materialized TaskItem when this occurrence has been persisted. Null for virtual occurrences.</summary>
        public Guid? MaterializedTaskItemId { get; init; }
        /// <summary>
        /// Exception-specific subtask overrides.
        /// Empty = inherit series template subtasks; non-empty = complete replacement list for this occurrence.
        /// </summary>
        public IReadOnlyList<RecurringSubtaskSyncItemDto> Subtasks { get; init; } = Array.Empty<RecurringSubtaskSyncItemDto>();
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }
}
