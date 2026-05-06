using System;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Per-user sequence allocator and retention watermark for the SyncChange feed.
    ///
    /// One row per user. Holds the next sequence number to hand out (<see cref="NextSequence"/>)
    /// and the lowest sequence still retained on disk after the retention sweep
    /// (<see cref="MinRetainedSequence"/>). The pull endpoint uses MinRetainedSequence to detect
    /// stale client cursors and signal that the device must re-bootstrap via /api/sync/snapshot.
    ///
    /// Internal to the Infrastructure layer. Application code must not depend on this type;
    /// it is reachable only through ISyncChangeRepository methods.
    /// </summary>
    public sealed class SyncSequenceState
    {
        public Guid UserId { get; private set; }

        /// <summary>
        /// Next sequence number to assign for this user. Reserved in batches by the SaveChanges
        /// interceptor under a row lock.
        /// </summary>
        public long NextSequence { get; private set; }

        /// <summary>
        /// The lowest <see cref="SyncChange.Sequence"/> still present in storage for this user.
        /// Advanced by the retention sweep after deleting prefix rows. A pull request whose
        /// AfterSequence is below this value indicates a stale cursor — the client must re-bootstrap.
        /// </summary>
        public long MinRetainedSequence { get; private set; }

        // EF Core
        private SyncSequenceState()
        {
        }

        public SyncSequenceState(Guid userId, long nextSequence, long minRetainedSequence)
        {
            UserId = userId;
            NextSequence = nextSequence;
            MinRetainedSequence = minRetainedSequence;
        }
    }
}
