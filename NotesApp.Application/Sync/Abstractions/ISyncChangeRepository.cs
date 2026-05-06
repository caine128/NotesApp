using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Sync.Abstractions
{
    /// <summary>
    /// Persistence contract for the SyncChange feed. Standalone (not based on IRepository&lt;T&gt;)
    /// because SyncChange does not inherit Entity&lt;Guid&gt; — it has no soft-delete or row version.
    /// </summary>
    public interface ISyncChangeRepository
    {
        /// <summary>
        /// Stages a new SyncChange for insertion. Sequence will be assigned by the SaveChanges
        /// interceptor at flush time. Caller's UnitOfWork commits.
        /// </summary>
        Task AddAsync(SyncChange change, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns up to <paramref name="limit"/> SyncChange rows for the user with
        /// <c>Sequence &gt; afterSequence</c>, ordered ascending. Caller passes <c>limit + 1</c>
        /// to detect HasMore.
        /// </summary>
        Task<IReadOnlyList<SyncChange>> GetAfterSequenceAsync(Guid userId,
                                                              long afterSequence,
                                                              int limit,
                                                              CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the user's <c>SyncSequenceState.MinRetainedSequence</c>, or 0 if no state row
        /// exists yet. Used by the pull handler to detect stale client cursors.
        /// </summary>
        Task<long> GetMinRetainedSequenceAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the highest <see cref="SyncChange.Sequence"/> currently stored for the user, or
        /// 0 if none. Used by the snapshot endpoint to capture the bootstrap watermark before the
        /// entity reads.
        /// </summary>
        Task<long> GetCurrentMaxSequenceAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
