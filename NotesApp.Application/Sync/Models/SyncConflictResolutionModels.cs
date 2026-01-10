using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Request body for resolving previously returned sync conflicts.
    /// </summary>
    public sealed record ResolveSyncConflictsRequestDto
    {
        public IReadOnlyList<SyncConflictResolutionDto> Resolutions { get; init; }
            = Array.Empty<SyncConflictResolutionDto>();
    }

    /// <summary>
    /// Resolution instruction for a single conflicted entity.
    /// 
    /// EntityType: "task" or "note"
    /// Choice: "keep_client" | "keep_server" | "merge"
    /// 
    /// For keep_client / merge, the corresponding TaskData / NoteData
    /// must be provided and will be treated as the desired final state.
    /// </summary>
    public sealed record SyncConflictResolutionDto
    {
        public SyncEntityType EntityType { get; init; }
        public Guid EntityId { get; init; }

        public SyncResolutionChoice Choice { get; init; }

        /// <summary>
        /// Version the client believes the entity is at when deciding the resolution.
        /// Used to detect second-level conflicts (server changed again).
        /// </summary>
        public long ExpectedVersion { get; init; }

        public TaskConflictResolutionDataDto? TaskData { get; init; }
        public NoteConflictResolutionDataDto? NoteData { get; init; }
    }

    /// <summary>
    /// Client-provided desired state for a task when resolving a conflict.
    /// Equivalent to the payload used for task updates.
    /// </summary>
    public sealed record TaskConflictResolutionDataDto
    {
        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public TimeOnly? StartTime { get; init; }
        public TimeOnly? EndTime { get; init; }
        public string? Location { get; init; }
        public TimeSpan? TravelTime { get; init; }
        public DateTime? ReminderAtUtc { get; init; }
    }

    /// <summary>
    /// Client-provided desired state for a note when resolving a conflict.
    /// </summary>
    public sealed record NoteConflictResolutionDataDto
    {
        public DateOnly Date { get; init; }
        public string? Title { get; init; }
        public string? Content { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }
    }

    /// <summary>
    /// Result for a single resolved conflict.
    /// </summary>
    public sealed record SyncConflictResolutionResultItemDto
    {
        public SyncEntityType EntityType { get; init; }
        public Guid EntityId { get; init; }

        /// <summary>
        /// e.g. "kept_server", "updated", "not_found", "deleted_on_server", "validation_failed", "conflict"
        /// </summary>
        public SyncConflictResolutionStatus Status { get; init; }

        public long? NewVersion { get; init; }

        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Overall response for a conflict resolution request.
    /// </summary>
    public sealed record ResolveSyncConflictsResultDto
    {
        public IReadOnlyList<SyncConflictResolutionResultItemDto> Results { get; init; }
            = Array.Empty<SyncConflictResolutionResultItemDto>();
    }
}
