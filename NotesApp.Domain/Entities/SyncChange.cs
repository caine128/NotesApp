using NotesApp.Domain.Common;
using System;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Append-only client-replication feed row. One row per user-visible mutation.
    ///
    /// Distinct from <see cref="OutboxMessage"/>: outbox is consumed by the server-side Worker for
    /// background work (notifications, embeddings); SyncChange is consumed by client devices via
    /// <c>GET /api/sync/pull</c>. Both are written in the same UnitOfWork as the underlying entity
    /// mutation for atomicity.
    ///
    /// Sequence is assigned by the SaveChanges interceptor at flush time (per-user
    /// transactionally-allocated long), not by SQL Server IDENTITY. Construction inserts a
    /// placeholder of 0; the interceptor overwrites it before the INSERT is sent.
    /// </summary>
    public sealed class SyncChange
    {
        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>
        /// Per-user monotonic sequence. Assigned by the SaveChanges interceptor; equals 0 until then.
        /// </summary>
        public long Sequence { get; private set; }

        public SyncEntityFamily EntityFamily { get; private set; }

        public Guid EntityId { get; private set; }

        public SyncOperation Operation { get; private set; }

        public DateTime ChangedAtUtc { get; private set; }

        /// <summary>
        /// Device that originated the mutation. Null for REST-originated and server-originated
        /// (e.g. conflict resolver) changes; set to the pushing device's id for sync push mutations.
        /// </summary>
        public Guid? OriginDeviceId { get; private set; }

        /// <summary>
        /// Self-contained client-replication payload. JSON-serialized output of the relevant
        /// per-family <c>ToSyncDto()</c> mapping for Created/Updated, or
        /// <c>{ "id": "...", "deletedAtUtc": "..." }</c> for Deleted.
        /// </summary>
        public string PayloadJson { get; private set; } = string.Empty;

        // EF Core
        private SyncChange()
        {
        }

        private SyncChange(Guid id,
                           Guid userId,
                           SyncEntityFamily entityFamily,
                           Guid entityId,
                           SyncOperation operation,
                           DateTime changedAtUtc,
                           Guid? originDeviceId,
                           string payloadJson)
        {
            Id = id;
            UserId = userId;
            Sequence = 0L;
            EntityFamily = entityFamily;
            EntityId = entityId;
            Operation = operation;
            ChangedAtUtc = changedAtUtc;
            OriginDeviceId = originDeviceId;
            PayloadJson = payloadJson;
        }

        /// <summary>
        /// Constructs a <see cref="SyncChange"/> from internal preconditions. Throws
        /// <see cref="ArgumentException"/> on precondition violation; failure is not a recoverable
        /// domain outcome here — callers (currently only <c>SyncChangeWriter</c>) source these
        /// fields from already-validated entities, so any failure indicates a programmer-error
        /// contract violation rather than a user-facing validation failure.
        /// </summary>
        public static SyncChange Create(Guid userId,
                                        SyncEntityFamily entityFamily,
                                        Guid entityId,
                                        SyncOperation operation,
                                        DateTime changedAtUtc,
                                        Guid? originDeviceId,
                                        string payloadJson)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("UserId must be a non-empty GUID.", nameof(userId));

            if (entityId == Guid.Empty)
                throw new ArgumentException("EntityId must be a non-empty GUID.", nameof(entityId));

            if (originDeviceId is { } d && d == Guid.Empty)
                throw new ArgumentException("OriginDeviceId, when present, must be a non-empty GUID.", nameof(originDeviceId));

            var normalizedPayload = payloadJson?.Trim() ?? string.Empty;
            if (normalizedPayload.Length == 0)
                throw new ArgumentException("Payload must be a non-empty string (typically JSON).", nameof(payloadJson));

            return new SyncChange(Guid.NewGuid(),
                                  userId,
                                  entityFamily,
                                  entityId,
                                  operation,
                                  changedAtUtc,
                                  originDeviceId,
                                  normalizedPayload);
        }
    }
}
