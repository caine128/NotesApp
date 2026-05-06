using NotesApp.Domain.Common;
using System;
using System.Text.Json;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// One row of the sequence-based sync pull feed. <see cref="Payload"/> carries the per-family
    /// snapshot DTO (e.g. TaskSyncItemDto) for Created/Updated, or
    /// <c>{ "id": "...", "deletedAtUtc": "..." }</c> for Deleted.
    /// </summary>
    public sealed record SyncPullItemDto(
        long Sequence,
        SyncEntityFamily EntityFamily,
        SyncOperation Operation,
        Guid EntityId,
        DateTime ChangedAtUtc,
        Guid? OriginDeviceId,
        JsonElement Payload);
}
