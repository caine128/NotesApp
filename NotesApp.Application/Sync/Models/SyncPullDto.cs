using System.Collections.Generic;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Response body for <c>GET /api/sync/pull</c>.
    /// </summary>
    /// <param name="Changes">Ordered list of changes after the requested AfterSequence.</param>
    /// <param name="NextSequence">Resume cursor: pass as AfterSequence on the next pull. Equals
    /// the last returned change's Sequence, or the original AfterSequence if no rows were returned.</param>
    /// <param name="HasMore">True iff more changes exist beyond this page (use NextSequence to fetch them).</param>
    public sealed record SyncPullDto(
        IReadOnlyList<SyncPullItemDto> Changes,
        long NextSequence,
        bool HasMore);
}
