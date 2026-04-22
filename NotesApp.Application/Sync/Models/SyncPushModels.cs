using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Top-level request body for sync push from a client device.
    /// This shape will later be bound directly from the API request.
    /// </summary>
    public sealed record SyncPushCommandPayloadDto
    {
        public Guid DeviceId { get; init; }

        /// <summary>
        /// Client-side timestamp (UTC) when this sync payload was assembled.
        /// Currently informational only, but kept for future diagnostics.
        /// </summary>
        public DateTime ClientSyncTimestampUtc { get; init; }

        public SyncPushTasksDto Tasks { get; init; } = new();
        public SyncPushNotesDto Notes { get; init; } = new();
        public SyncPushBlocksDto Blocks { get; init; } = new();
        // REFACTORED: added category push collections for task categories feature
        public SyncPushCategoriesDto Categories { get; init; } = new();
        // REFACTORED: added subtask push collections for subtasks feature
        public SyncPushSubtasksDto Subtasks { get; init; } = new();

        // REFACTORED: added attachment push collections for task-attachments feature
        /// <summary>
        /// Attachment deletions from the client device.
        /// Note: Attachment uploads always go through the REST endpoint
        /// (POST /api/attachments/{taskId}); the outbox propagates them to sync pull.
        /// Only deletions are synced via push.
        /// </summary>
        public SyncPushAttachmentsDto Attachments { get; init; } = new();

        // REFACTORED: added recurring-task push collections for recurring-tasks feature
        /// <summary>
        /// Recurring root creates/deletes from the client device.
        /// Processed before RecurringSeries so within-push RootClientId references resolve.
        /// </summary>
        public SyncPushRecurringRootsDto RecurringRoots { get; init; } = new();

        /// <summary>
        /// Recurring series creates/updates/deletes from the client device.
        /// Processed before RecurringExceptions so within-push SeriesClientId references resolve.
        /// </summary>
        public SyncPushRecurringSeriesDto RecurringSeries { get; init; } = new();

        /// <summary>
        /// Recurring series subtask creates/updates/deletes from the client device.
        /// Covers both series template subtasks and exception subtask overrides.
        /// </summary>
        public SyncPushRecurringSeriesSubtasksDto RecurringSeriesSubtasks { get; init; } = new();

        /// <summary>
        /// Recurring exception creates/updates/deletes from the client device.
        /// Processed after RecurringSeries so SeriesId references are available.
        /// </summary>
        public SyncPushRecurringExceptionsDto RecurringExceptions { get; init; } = new();
    }

    // ----------------------------
    // Tasks: request DTOs
    // ----------------------------

    public sealed record SyncPushTasksDto
    {
        public IReadOnlyList<TaskCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<TaskUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<TaskDeletedPushItemDto> Deleted { get; init; } = [];
    }

    public sealed record TaskCreatedPushItemDto
    {
        /// <summary>
        /// Client-generated id for correlation. The server will map this to a server-side id.
        /// </summary>
        public Guid ClientId { get; init; }

        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public DateTime? ReminderAtUtc { get; init; }
        // REFACTORED: added CategoryId for task categories feature
        /// <summary>
        /// Optional category to assign to this task. Null means uncategorised.
        /// If the category was also created in this push, supply its ClientId here —
        /// the handler resolves it to the server-side Id via categoryClientToServerIds.
        /// </summary>
        public Guid? CategoryId { get; init; }

        // REFACTORED: added Priority for task priority feature
        /// <summary>
        /// Priority level for this task. Defaults to Normal when not specified.
        /// </summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;

        // REFACTORED: added MeetingLink for meeting-link feature
        /// <summary>
        /// Optional join URL or dial-in reference for a meeting. Null means no meeting link.
        /// </summary>
        public string? MeetingLink { get; init; }

        // REFACTORED: added recurring-task link fields for recurring-tasks feature
        /// <summary>
        /// Optional server ID of the recurring series this occurrence belongs to.
        /// When set, <see cref="CanonicalOccurrenceDate"/> must also be provided.
        /// Null for standalone (non-recurring) tasks.
        /// If the series was created in the same push, use <see cref="RecurringSeriesClientId"/> instead.
        /// </summary>
        public Guid? RecurringSeriesId { get; init; }

        /// <summary>
        /// Client ID of the recurring series if it was created in the same push.
        /// The handler resolves it to the server-side Id via seriesClientToServerIds.
        /// </summary>
        public Guid? RecurringSeriesClientId { get; init; }

        /// <summary>
        /// The canonical (recurrence-engine-generated) date for this occurrence.
        /// Required when <see cref="RecurringSeriesId"/> or <see cref="RecurringSeriesClientId"/> is set.
        /// </summary>
        public DateOnly? CanonicalOccurrenceDate { get; init; }
    }

    public sealed record TaskUpdatedPushItemDto
    {
        /// <summary>
        /// Server id of the task.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Version the client believes the entity is at.
        /// Used for optimistic concurrency.
        /// </summary>
        public long ExpectedVersion { get; init; }
        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public DateTime? ReminderAtUtc { get; init; }
        // REFACTORED: added CategoryId for task categories feature
        /// <summary>
        /// Optional category to assign to this task. Null means uncategorised or unchanged.
        /// If the category was created in the same push, supply its ClientId here —
        /// the handler resolves it via categoryClientToServerIds.
        /// </summary>
        public Guid? CategoryId { get; init; }

        // REFACTORED: added Priority for task priority feature
        /// <summary>
        /// Priority level for this task. Defaults to Normal when not specified.
        /// </summary>
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;

        // REFACTORED: added MeetingLink for meeting-link feature
        /// <summary>
        /// Optional join URL or dial-in reference for a meeting. Null clears any existing link.
        /// </summary>
        public string? MeetingLink { get; init; }
    }

    public sealed record TaskDeletedPushItemDto
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Optional version for stronger delete semantics.
        /// Currently not enforced; we use "delete wins" semantics on the server.
        /// </summary>
        public long? ExpectedVersion { get; init; }
    }

    // ----------------------------
    // Notes: request DTOs
    // ----------------------------

    public sealed record SyncPushNotesDto
    {
        public IReadOnlyList<NoteCreatedPushItemDto> Created { get; init; } = Array.Empty<NoteCreatedPushItemDto>();
        public IReadOnlyList<NoteUpdatedPushItemDto> Updated { get; init; } = Array.Empty<NoteUpdatedPushItemDto>();
        public IReadOnlyList<NoteDeletedPushItemDto> Deleted { get; init; } = Array.Empty<NoteDeletedPushItemDto>();
    }

    public sealed record NoteCreatedPushItemDto
    {
        public Guid ClientId { get; init; }

        public DateOnly Date { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }
    }

    public sealed record NoteUpdatedPushItemDto
    {
        public Guid Id { get; init; }
        public long ExpectedVersion { get; init; }

        public DateOnly Date { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }
    }

    public sealed record NoteDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }



    // ----------------------------
    // Blocks: request DTOs
    // ----------------------------

    public sealed record SyncPushBlocksDto
    {
        public IReadOnlyList<BlockCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<BlockUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<BlockDeletedPushItemDto> Deleted { get; init; } = [];
    }

    public sealed record BlockCreatedPushItemDto
    {
        /// <summary>
        /// Client-generated id for correlation. The server will map this to a server-side id.
        /// </summary>
        public Guid ClientId { get; init; }

        /// <summary>
        /// Server ID of the parent (Note or Task).
        /// If the parent was also created in this push, use ParentClientId instead.
        /// </summary>
        public Guid? ParentId { get; init; }

        /// <summary>
        /// Client ID of the parent if it was created in this same push.
        /// Server will resolve to server ID after parent is created.
        /// </summary>
        public Guid? ParentClientId { get; init; }

        public BlockParentType ParentType { get; init; }
        public BlockType Type { get; init; }
        public string Position { get; init; } = string.Empty;

        /// <summary>
        /// Text content for text blocks (Paragraph, Heading, etc.).
        /// </summary>
        public string? TextContent { get; init; }

        /// <summary>
        /// Client-generated identifier for tracking asset upload (for Image/File blocks).
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
    }

    public sealed record BlockUpdatedPushItemDto
    {
        /// <summary>
        /// Server id of the block.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Version the client believes the entity is at.
        /// Used for optimistic concurrency.
        /// </summary>
        public long ExpectedVersion { get; init; }

        /// <summary>
        /// New position (fractional index). Null means no change.
        /// </summary>
        public string? Position { get; init; }

        /// <summary>
        /// New text content. Null means no change (for text blocks).
        /// </summary>
        public string? TextContent { get; init; }
    }

    public sealed record BlockDeletedPushItemDto
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Optional version for stronger delete semantics.
        /// Currently not enforced; we use "delete wins" semantics.
        /// </summary>
        public long? ExpectedVersion { get; init; }
    }





    // ----------------------------
    // Result DTOs
    // ----------------------------

    public sealed record SyncPushResultDto
    {
        public SyncPushTasksResultDto Tasks { get; init; } = new();
        public SyncPushNotesResultDto Notes { get; init; } = new();
        public SyncPushBlocksResultDto Blocks { get; init; } = new();
        // REFACTORED: added category results for task categories feature
        public SyncPushCategoriesResultDto Categories { get; init; } = new();
        // REFACTORED: added subtask results for subtasks feature
        public SyncPushSubtasksResultDto Subtasks { get; init; } = new();

        // REFACTORED: added attachment results for task-attachments feature
        public SyncPushAttachmentsResultDto Attachments { get; init; } = new();

        // REFACTORED: added recurring-task results for recurring-tasks feature
        public SyncPushRecurringRootsResultDto RecurringRoots { get; init; } = new();
        public SyncPushRecurringSeriesResultDto RecurringSeries { get; init; } = new();
        public SyncPushRecurringSeriesSubtasksResultDto RecurringSeriesSubtasks { get; init; } = new();
        public SyncPushRecurringExceptionsResultDto RecurringExceptions { get; init; } = new();
    }

    public sealed record SyncPushTasksResultDto
    {
        public IReadOnlyList<TaskCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<TaskUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<TaskDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record SyncPushNotesResultDto
    {
        public IReadOnlyList<NoteCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<NoteUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<NoteDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record SyncPushBlocksResultDto
    {
        public IReadOnlyList<BlockCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<BlockUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<BlockDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record TaskCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status is Failed. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record TaskUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record TaskDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record NoteCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }

        // <summary>
        /// Conflict details when Status is Failed. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record NoteUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record NoteDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }


    public sealed record BlockCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status is Failed. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record BlockUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record BlockDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>
        /// Conflict details when Status indicates a failure. Null for successful operations.
        /// </summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // Conflict Detail DTO
    // ----------------------------

    /// <summary>
    /// Details about a conflict or problem encountered while processing a sync push item.
    /// Embedded directly in each result DTO when a failure occurs.
    /// </summary>
    public sealed record SyncPushConflictDetailDto
    {
        /// <summary>
        /// The type of conflict that occurred.
        /// </summary>
        public SyncConflictType ConflictType { get; init; }

        /// <summary>
        /// Client's expected version (for version mismatch conflicts).
        /// </summary>
        public long? ClientVersion { get; init; }

        /// <summary>
        /// Server's current version (for version mismatch conflicts).
        /// </summary>
        public long? ServerVersion { get; init; }

        /// <summary>
        /// Server-side task state at conflict time (for version mismatch on tasks).
        /// </summary>
        public TaskSyncItemDto? ServerTask { get; init; }

        /// <summary>
        /// Server-side note state at conflict time (for version mismatch on notes).
        /// </summary>
        public NoteSyncItemDto? ServerNote { get; init; }

        /// <summary>
        /// Server-side block state at conflict time (for version mismatch on blocks).
        /// </summary>
        public BlockSyncItemDto? ServerBlock { get; init; }

        // REFACTORED: added ServerCategory for category version-mismatch conflicts
        /// <summary>
        /// Server-side category state at conflict time (for version mismatch on categories).
        /// </summary>
        public CategorySyncItemDto? ServerCategory { get; init; }

        // REFACTORED: added ServerSubtask for subtask version-mismatch conflicts
        /// <summary>
        /// Server-side subtask state at conflict time (for version mismatch on subtasks).
        /// </summary>
        public SubtaskSyncItemDto? ServerSubtask { get; init; }

        /// <summary>
        /// Validation or other error messages.
        /// </summary>
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }

    // ----------------------------
    // REFACTORED: Categories — request DTOs (task categories feature)
    // ----------------------------

    /// <summary>
    /// Category push collections from the client device.
    /// Processed before tasks so that within-push CategoryId references resolve correctly.
    /// </summary>
    public sealed record SyncPushCategoriesDto
    {
        public IReadOnlyList<CategoryCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<CategoryUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<CategoryDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>
    /// A new category the client wants to create on the server.
    /// The server assigns a stable server-side Id and returns it in the result.
    /// </summary>
    public sealed record CategoryCreatedPushItemDto
    {
        /// <summary>
        /// Client-generated id for correlation. The server maps this to a server-side Id
        /// and stores it in categoryClientToServerIds so task items in the same push
        /// can reference it by ClientId.
        /// </summary>
        public Guid ClientId { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// A rename (or other update) the client wants to apply to an existing category.
    /// </summary>
    public sealed record CategoryUpdatedPushItemDto
    {
        /// <summary>Server id of the category.</summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Version the client believes the entity is at.
        /// Used for optimistic concurrency — mismatch returns a VersionMismatch conflict.
        /// </summary>
        public long ExpectedVersion { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// A category deletion initiated by the mobile client.
    /// The client is responsible for sending the affected task updates
    /// (CategoryId = null, incremented version) in the same push payload.
    /// The server does NOT cascade: it processes the task updates independently.
    /// </summary>
    public sealed record CategoryDeletedPushItemDto
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Optional version for stronger delete semantics.
        /// Currently not enforced on delete — "delete wins" semantics apply.
        /// </summary>
        public long? ExpectedVersion { get; init; }
    }

    // ----------------------------
    // REFACTORED: Categories — result DTOs (task categories feature)
    // ----------------------------

    public sealed record SyncPushCategoriesResultDto
    {
        public IReadOnlyList<CategoryCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<CategoryUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<CategoryDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record CategoryCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }

        /// <summary>Conflict details when Status is Failed. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record CategoryUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }

        /// <summary>Conflict details when Status indicates a failure. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record CategoryDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>Conflict details when Status indicates a failure. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Subtasks — request DTOs (subtasks feature)
    // ----------------------------

    /// <summary>
    /// Subtask push collections from the client device.
    /// Processed after tasks so that within-push TaskId references resolve correctly.
    /// </summary>
    public sealed record SyncPushSubtasksDto
    {
        public IReadOnlyList<SubtaskCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<SubtaskUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<SubtaskDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>
    /// A new subtask the client wants to create on the server.
    /// The server assigns a stable server-side Id and returns it in the result.
    /// </summary>
    public sealed record SubtaskCreatedPushItemDto
    {
        /// <summary>
        /// Client-generated id for correlation. The server maps this to a server-side Id.
        /// </summary>
        public Guid ClientId { get; init; }

        /// <summary>
        /// Server ID of the parent task if it already exists on the server.
        /// If the parent task was also created in this push, use TaskClientId instead.
        /// </summary>
        public Guid? TaskId { get; init; }

        /// <summary>
        /// Client ID of the parent task if it was created in this same push.
        /// Server resolves to the server-side Id via taskClientToServerIds.
        /// </summary>
        public Guid? TaskClientId { get; init; }

        public string Text { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }

        /// <summary>
        /// Fractional-index position string for ordering within the parent task.
        /// </summary>
        public string Position { get; init; } = string.Empty;
    }

    /// <summary>
    /// An update (text, completion, or reorder) the client wants to apply to an existing subtask.
    /// Null fields mean "no change" — mirrors the BlockUpdatedPushItemDto pattern.
    /// </summary>
    public sealed record SubtaskUpdatedPushItemDto
    {
        /// <summary>Server id of the subtask.</summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Version the client believes the entity is at.
        /// Used for optimistic concurrency — mismatch returns a VersionMismatch conflict.
        /// </summary>
        public long ExpectedVersion { get; init; }

        /// <summary>New text. Null means no change.</summary>
        public string? Text { get; init; }

        /// <summary>New completion state. Null means no change.</summary>
        public bool? IsCompleted { get; init; }

        /// <summary>New fractional-index position. Null means no change.</summary>
        public string? Position { get; init; }
    }

    /// <summary>
    /// A subtask deletion initiated by the mobile client.
    /// </summary>
    public sealed record SubtaskDeletedPushItemDto
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Optional version for stronger delete semantics.
        /// Currently not enforced on delete — "delete wins" semantics apply.
        /// </summary>
        public long? ExpectedVersion { get; init; }
    }

    // ----------------------------
    // REFACTORED: Subtasks — result DTOs (subtasks feature)
    // ----------------------------

    public sealed record SyncPushSubtasksResultDto
    {
        public IReadOnlyList<SubtaskCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<SubtaskUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<SubtaskDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record SubtaskCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }

        /// <summary>Conflict details when Status is Failed. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record SubtaskUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }

        /// <summary>Conflict details when Status indicates a failure. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record SubtaskDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>Conflict details when Status indicates a failure. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Attachments — request and result DTOs (task-attachments feature)
    // ----------------------------

    /// <summary>
    /// Attachment push collections from the client device.
    /// Only deletions are included — uploads always go through the REST endpoint
    /// (POST /api/attachments/{taskId}); the outbox propagates them to sync pull.
    /// </summary>
    public sealed record SyncPushAttachmentsDto
    {
        public IReadOnlyList<AttachmentDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>
    /// An attachment deletion initiated by the mobile client.
    /// </summary>
    public sealed record AttachmentDeletedPushItemDto
    {
        public Guid Id { get; init; }

        /// <summary>
        /// Optional version for stronger delete semantics.
        /// Currently not enforced on delete — "delete wins" semantics apply.
        /// </summary>
        public long? ExpectedVersion { get; init; }
    }

    public sealed record SyncPushAttachmentsResultDto
    {
        public IReadOnlyList<AttachmentDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record AttachmentDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }

        /// <summary>Conflict details when Status indicates a failure. Null for successful operations.</summary>
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Recurring Roots — request and result DTOs (recurring-tasks feature)
    // ----------------------------

    /// <summary>
    /// Recurring root push from the client device.
    /// Roots are thin identity anchors — only Created and Deleted are supported.
    /// </summary>
    public sealed record SyncPushRecurringRootsDto
    {
        public IReadOnlyList<RecurringRootCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringRootDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>A new recurring root the client wants to create on the server.</summary>
    public sealed record RecurringRootCreatedPushItemDto
    {
        /// <summary>Client-generated id for correlation. Returned in the result and used by RecurringSeries.RootClientId.</summary>
        public Guid ClientId { get; init; }
    }

    /// <summary>A recurring root deletion initiated by the mobile client (delete-all scope).</summary>
    public sealed record RecurringRootDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }

    public sealed record SyncPushRecurringRootsResultDto
    {
        public IReadOnlyList<RecurringRootCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringRootDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record RecurringRootCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringRootDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Recurring Series — request and result DTOs (recurring-tasks feature)
    // ----------------------------

    public sealed record SyncPushRecurringSeriesDto
    {
        public IReadOnlyList<RecurringSeriesCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringSeriesUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringSeriesDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>A new recurring series segment the client wants to create on the server.</summary>
    public sealed record RecurringSeriesCreatedPushItemDto
    {
        /// <summary>Client-generated id for correlation. Used by RecurringSeriesSubtasks.SeriesClientId.</summary>
        public Guid ClientId { get; init; }
        /// <summary>Server ID of the parent root (if the root already exists on the server).</summary>
        public Guid? RootId { get; init; }
        /// <summary>Client ID of the parent root if it was created in the same push.</summary>
        public Guid? RootClientId { get; init; }
        // Recurrence rule
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
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;
        public string? MeetingLink { get; init; }
        public int? ReminderOffsetMinutes { get; init; }
    }

    /// <summary>
    /// A template-field update or termination the client wants to apply to an existing series.
    /// Set EndsBeforeDate to terminate the series (ThisAndFollowing split).
    /// </summary>
    public sealed record RecurringSeriesUpdatedPushItemDto
    {
        public Guid Id { get; init; }
        public long ExpectedVersion { get; init; }
        // Template task fields (all required — client sends full state)
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public Guid? CategoryId { get; init; }
        public TaskPriority Priority { get; init; } = TaskPriority.Normal;
        public string? MeetingLink { get; init; }
        public int? ReminderOffsetMinutes { get; init; }
        /// <summary>When set, terminates this series at this exclusive date (ThisAndFollowing split).</summary>
        public DateOnly? EndsBeforeDate { get; init; }
    }

    /// <summary>A recurring series deletion initiated by the mobile client.</summary>
    public sealed record RecurringSeriesDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }

    public sealed record SyncPushRecurringSeriesResultDto
    {
        public IReadOnlyList<RecurringSeriesCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringSeriesUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringSeriesDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record RecurringSeriesCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringSeriesUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringSeriesDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Recurring Series Subtasks — request and result DTOs (recurring-tasks feature)
    // ----------------------------

    /// <summary>
    /// Recurring series subtask push from the client device.
    /// Covers both series template subtasks (SeriesId set) and exception overrides (ExceptionId set).
    /// </summary>
    public sealed record SyncPushRecurringSeriesSubtasksDto
    {
        public IReadOnlyList<RecurringSubtaskCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringSubtaskUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringSubtaskDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>A new recurring subtask (series template or exception override) the client wants to create.</summary>
    public sealed record RecurringSubtaskCreatedPushItemDto
    {
        public Guid ClientId { get; init; }
        /// <summary>Server ID of the parent series (when this is a template subtask).</summary>
        public Guid? SeriesId { get; init; }
        /// <summary>Client ID of the parent series if it was created in the same push.</summary>
        public Guid? SeriesClientId { get; init; }
        /// <summary>Server ID of the parent exception (when this is an exception override subtask).</summary>
        public Guid? ExceptionId { get; init; }
        public string Text { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
    }

    /// <summary>An update to an existing recurring subtask.</summary>
    public sealed record RecurringSubtaskUpdatedPushItemDto
    {
        public Guid Id { get; init; }
        public long ExpectedVersion { get; init; }
        /// <summary>New text. Null means no change.</summary>
        public string? Text { get; init; }
        /// <summary>New position. Null means no change.</summary>
        public string? Position { get; init; }
        /// <summary>New completion state. Null means no change.</summary>
        public bool? IsCompleted { get; init; }
    }

    /// <summary>A recurring subtask deletion initiated by the mobile client.</summary>
    public sealed record RecurringSubtaskDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }

    public sealed record SyncPushRecurringSeriesSubtasksResultDto
    {
        public IReadOnlyList<RecurringSubtaskCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringSubtaskUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringSubtaskDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record RecurringSubtaskCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringSubtaskUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringSubtaskDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    // ----------------------------
    // REFACTORED: Recurring Exceptions — request and result DTOs (recurring-tasks feature)
    // ----------------------------

    public sealed record SyncPushRecurringExceptionsDto
    {
        public IReadOnlyList<RecurringExceptionCreatedPushItemDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringExceptionUpdatedPushItemDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringExceptionDeletedPushItemDto> Deleted { get; init; } = [];
    }

    /// <summary>
    /// A new recurring exception the client wants to create.
    /// The server upserts by (SeriesId, OccurrenceDate) — if one already exists it is updated.
    /// </summary>
    public sealed record RecurringExceptionCreatedPushItemDto
    {
        public Guid ClientId { get; init; }
        /// <summary>Server ID of the parent series.</summary>
        public Guid SeriesId { get; init; }
        public DateOnly OccurrenceDate { get; init; }
        /// <summary>True = deletion tombstone (occurrence skipped). False = field override.</summary>
        public bool IsDeletion { get; init; }
        // Override fields (null = inherit from series template; only used when IsDeletion = false)
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
        /// <summary>
        /// Completion state for this specific occurrence.
        /// Ignored when <see cref="IsDeletion"/> is true.
        /// False = not completed (default).
        /// </summary>
        public bool IsCompleted { get; init; }
    }

    /// <summary>An update to an existing recurring exception by server ID.</summary>
    public sealed record RecurringExceptionUpdatedPushItemDto
    {
        public Guid Id { get; init; }
        public long ExpectedVersion { get; init; }
        // Override fields (null = inherit from series template)
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
        /// <summary>
        /// New completion state for this occurrence. Null = no change (leave as-is on the server).
        /// </summary>
        public bool? IsCompleted { get; init; }
    }

    /// <summary>A recurring exception deletion initiated by the mobile client.</summary>
    public sealed record RecurringExceptionDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }

    public sealed record SyncPushRecurringExceptionsResultDto
    {
        public IReadOnlyList<RecurringExceptionCreatedPushResultDto> Created { get; init; } = [];
        public IReadOnlyList<RecurringExceptionUpdatedPushResultDto> Updated { get; init; } = [];
        public IReadOnlyList<RecurringExceptionDeletedPushResultDto> Deleted { get; init; } = [];
    }

    public sealed record RecurringExceptionCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public SyncPushCreatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringExceptionUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public SyncPushUpdatedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }

    public sealed record RecurringExceptionDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public SyncPushDeletedStatus Status { get; init; }
        public SyncPushConflictDetailDto? Conflict { get; init; }
    }
}
