using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Sync.Abstractions
{
    /// <summary>
    /// Centralized writer for the SyncChange feed. Serializes the appropriate per-family payload
    /// and stages a SyncChange for insertion via <see cref="ISyncChangeRepository"/>. Never calls
    /// SaveChanges — the caller's UnitOfWork commits entity mutation, outbox message, and
    /// SyncChange row atomically.
    ///
    /// CONTRACT: callers MUST NOT perform external IO (blob upload, HTTP, etc.) between the first
    /// writer invocation and SaveChangesAsync. The interceptor takes a row lock on
    /// SyncSequenceStates inside that transaction; holding it across external IO would block
    /// sibling devices for the same user.
    /// </summary>
    public interface ISyncChangeWriter
    {
        Task AddCreatedAsync(TaskItem entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(TaskItem entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(Note entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(Note entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(Block entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(Block entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(Asset entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(TaskCategory entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(TaskCategory entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(Subtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(Subtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(Attachment entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(RecurringTaskRoot entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(RecurringTaskSeries entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(RecurringTaskSeries entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(RecurringTaskSubtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(RecurringTaskSubtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(RecurringTaskException entity, Guid? originDeviceId, CancellationToken cancellationToken = default);
        Task AddUpdatedAsync(RecurringTaskException entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        Task AddCreatedAsync(RecurringTaskAttachment entity, Guid? originDeviceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Emits a Deleted SyncChange. Payload is <c>{ "id": "...", "deletedAtUtc": "..." }</c>.
        /// Use this regardless of whether the deletion is a soft-delete on the original row or a
        /// hard-delete: clients reconcile state by family + id.
        /// </summary>
        Task AddDeletedAsync(SyncEntityFamily family,
                             Guid entityId,
                             Guid userId,
                             Guid? originDeviceId,
                             CancellationToken cancellationToken = default);
    }
}
