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
}
