using FluentResults;

namespace NotesApp.Application.Sync
{
    /// <summary>
    /// Structured FluentResults errors for the sync feed.
    /// </summary>
    public static class SyncErrors
    {
        /// <summary>Error code for stale-cursor: client must re-bootstrap via /api/sync/snapshot.</summary>
        public const string CursorStaleCode = "Sync.CursorStale";

        /// <summary>
        /// The client's <c>afterSequence</c> is below the user's <c>MinRetainedSequence</c>; the
        /// rows it would have requested have been pruned by the retention sweep. The client must
        /// re-bootstrap via <c>GET /api/sync/snapshot</c>.
        /// </summary>
        public static Error CursorStale(long requested, long minRetained) =>
            new Error("Sync cursor is stale; client must re-bootstrap via /api/sync/snapshot.")
                .WithMetadata("ErrorCode", CursorStaleCode)
                .WithMetadata("RequestedAfterSequence", requested)
                .WithMetadata("MinRetainedSequence", minRetained);
    }
}
