using System.Collections.Generic;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Bootstrap snapshot returned by <c>GET /api/sync/snapshot</c>. Contains the current state of
    /// every non-deleted entity owned by the user, plus the <see cref="BootstrapSequence"/> that
    /// the client should persist as <c>lastServerSequence</c>. Subsequent incremental updates flow
    /// through <c>GET /api/sync/pull?afterSequence={BootstrapSequence}</c>.
    /// </summary>
    public sealed record SyncSnapshotDto(
        IReadOnlyList<SyncSnapshotItemDto> Items,
        long BootstrapSequence);
}
