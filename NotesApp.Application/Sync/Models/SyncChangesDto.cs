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
        public string? Content { get; init; }
        public string? Summary { get; init; }
        public string? Tags { get; init; }

        public long Version { get; init; }

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
