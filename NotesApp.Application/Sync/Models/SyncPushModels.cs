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
    }

    // ----------------------------
    // Tasks: request DTOs
    // ----------------------------

    public sealed record SyncPushTasksDto
    {
        public IReadOnlyList<TaskCreatedPushItemDto> Created { get; init; } = Array.Empty<TaskCreatedPushItemDto>();
        public IReadOnlyList<TaskUpdatedPushItemDto> Updated { get; init; } = Array.Empty<TaskUpdatedPushItemDto>();
        public IReadOnlyList<TaskDeletedPushItemDto> Deleted { get; init; } = Array.Empty<TaskDeletedPushItemDto>();
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
        public string? Content { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }
    }

    public sealed record NoteUpdatedPushItemDto
    {
        public Guid Id { get; init; }
        public long ExpectedVersion { get; init; }

        public DateOnly Date { get; init; }
        public string? Title { get; init; }
        public string? Content { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }
    }

    public sealed record NoteDeletedPushItemDto
    {
        public Guid Id { get; init; }
        public long? ExpectedVersion { get; init; }
    }

    // ----------------------------
    // Result DTOs
    // ----------------------------

    public sealed record SyncPushResultDto
    {
        public SyncPushTasksResultDto Tasks { get; init; } = new();
        public SyncPushNotesResultDto Notes { get; init; } = new();

        /// <summary>
        /// Per-entity conflicts collected during processing (version mismatch,
        /// not found, deleted on server, validation failures, etc.).
        /// </summary>
        public IReadOnlyList<SyncConflictDto> Conflicts { get; init; } = Array.Empty<SyncConflictDto>();
    }

    public sealed record SyncPushTasksResultDto
    {
        public IReadOnlyList<TaskCreatedPushResultDto> Created { get; init; } = Array.Empty<TaskCreatedPushResultDto>();
        public IReadOnlyList<TaskUpdatedPushResultDto> Updated { get; init; } = Array.Empty<TaskUpdatedPushResultDto>();
        public IReadOnlyList<TaskDeletedPushResultDto> Deleted { get; init; } = Array.Empty<TaskDeletedPushResultDto>();
    }

    public sealed record SyncPushNotesResultDto
    {
        public IReadOnlyList<NoteCreatedPushResultDto> Created { get; init; } = Array.Empty<NoteCreatedPushResultDto>();
        public IReadOnlyList<NoteUpdatedPushResultDto> Updated { get; init; } = Array.Empty<NoteUpdatedPushResultDto>();
        public IReadOnlyList<NoteDeletedPushResultDto> Deleted { get; init; } = Array.Empty<NoteDeletedPushResultDto>();
    }

    public sealed record TaskCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public string Status { get; init; } = string.Empty; // e.g. "created", "failed"
    }

    public sealed record TaskUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public string Status { get; init; } = string.Empty; // e.g. "updated", "conflict", "not_found"
    }

    public sealed record TaskDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public string Status { get; init; } = string.Empty; // "deleted", "already_deleted", "not_found"
    }

    public sealed record NoteCreatedPushResultDto
    {
        public Guid ClientId { get; init; }
        public Guid ServerId { get; init; }
        public long Version { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    public sealed record NoteUpdatedPushResultDto
    {
        public Guid Id { get; init; }
        public long? NewVersion { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    public sealed record NoteDeletedPushResultDto
    {
        public Guid Id { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    /// <summary>
    /// Describes a conflict or problem encountered while processing a push item.
    /// </summary>
    public sealed record SyncConflictDto
    {
        public string EntityType { get; init; } = string.Empty; // "task" or "note"
        public Guid? EntityId { get; init; }

        /// <summary>
        /// e.g. "version_mismatch", "not_found", "deleted_on_server", "validation_failed"
        /// </summary>
        public string ConflictType { get; init; } = string.Empty;

        public long? ClientVersion { get; init; }
        public long? ServerVersion { get; init; }

        /// <summary>
        /// Optional server-side state snapshot for the entity at the time
        /// of the conflict (for "version_mismatch" scenarios).
        /// </summary>
        public TaskSyncItemDto? ServerTask { get; init; }
        public NoteSyncItemDto? ServerNote { get; init; }

        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }
}
