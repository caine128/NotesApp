using NotesApp.Domain.Common;
using System;
using System.Text.Json;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// One entity in a sync snapshot. Same payload semantics as <see cref="SyncPullItemDto"/>'s
    /// payload (per-family <c>ToSyncDto()</c> shape) but without change-specific fields, since
    /// snapshot rows represent current state, not history.
    /// </summary>
    public sealed record SyncSnapshotItemDto(
        SyncEntityFamily EntityFamily,
        Guid EntityId,
        JsonElement Payload);
}
