using NotesApp.Application.Common;
using NotesApp.Application.RecurringAttachments;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Sync
{
    /// <summary>
    /// Default implementation of <see cref="ISyncChangeWriter"/>.
    /// Serializes the per-family payload via existing <c>ToSyncDto()</c> mappings and stages a
    /// <see cref="SyncChange"/> via the repository. Never calls SaveChanges.
    ///
    /// TODO (deferred): if Block.TextContent ever becomes large enough to bloat SyncChanges rows,
    /// snapshot only metadata + version for BlockType.Text blocks and require the client to GET
    /// /api/blocks/{id} for full content. Defer until measured.
    /// </summary>
    public sealed class SyncChangeWriter : ISyncChangeWriter
    {
        private readonly ISyncChangeRepository _repository;
        private readonly ISystemClock _clock;

        // Web defaults: camelCase, ignore null on serialize, etc. Single shared instance is safe
        // (JsonSerializerOptions is thread-safe after first use).
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public SyncChangeWriter(ISyncChangeRepository repository, ISystemClock clock)
        {
            _repository = repository;
            _clock = clock;
        }

        public Task AddCreatedAsync(TaskItem entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Task, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(TaskItem entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Task, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(Note entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Note, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(Note entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Note, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(Block entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Block, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(Block entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Block, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(Asset entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Asset, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(TaskCategory entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Category, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(TaskCategory entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Category, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(Subtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Subtask, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(Subtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Subtask, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(Attachment entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.Attachment, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(RecurringTaskRoot entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskRoot, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(RecurringTaskSeries entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskSeries, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(RecurringTaskSeries entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskSeries, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(RecurringTaskSubtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskSubtask, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddUpdatedAsync(RecurringTaskSubtask entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskSubtask, entity.Id, SyncOperation.Updated,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddCreatedAsync(RecurringTaskException entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskException, entity.Id, SyncOperation.Created,
                          originDeviceId, SerializeRecurringException(entity), cancellationToken);

        public Task AddUpdatedAsync(RecurringTaskException entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskException, entity.Id, SyncOperation.Updated,
                          originDeviceId, SerializeRecurringException(entity), cancellationToken);

        public Task AddCreatedAsync(RecurringTaskAttachment entity, Guid? originDeviceId, CancellationToken cancellationToken = default)
            => StageAsync(entity.UserId, SyncEntityFamily.RecurringTaskAttachment, entity.Id, SyncOperation.Created,
                          originDeviceId, JsonSerializer.Serialize(entity.ToSyncDto(), JsonOptions), cancellationToken);

        public Task AddDeletedAsync(SyncEntityFamily family,
                                    Guid entityId,
                                    Guid userId,
                                    Guid? originDeviceId,
                                    CancellationToken cancellationToken = default)
        {
            var utcNow = _clock.UtcNow;
            var payload = JsonSerializer.Serialize(new DeletedPayload(entityId, utcNow), JsonOptions);
            return StageAsyncWithTime(userId, family, entityId, SyncOperation.Deleted,
                                      originDeviceId, payload, utcNow, cancellationToken);
        }

        private async Task StageAsync(Guid userId,
                                      SyncEntityFamily family,
                                      Guid entityId,
                                      SyncOperation operation,
                                      Guid? originDeviceId,
                                      string payloadJson,
                                      CancellationToken cancellationToken)
        {
            await StageAsyncWithTime(userId, family, entityId, operation, originDeviceId, payloadJson, _clock.UtcNow, cancellationToken);
        }

        private async Task StageAsyncWithTime(Guid userId,
                                              SyncEntityFamily family,
                                              Guid entityId,
                                              SyncOperation operation,
                                              Guid? originDeviceId,
                                              string payloadJson,
                                              DateTime utcNow,
                                              CancellationToken cancellationToken)
        {
            var result = SyncChange.Create(userId, family, entityId, operation, utcNow, originDeviceId, payloadJson);
            if (result.IsFailure || result.Value is null)
            {
                throw new InvalidOperationException(
                    "SyncChangeWriter failed to construct SyncChange: " +
                    string.Join(", ", result.Errors.Select(e => $"{e.Code}:{e.Message}")));
            }

            await _repository.AddAsync(result.Value, cancellationToken);
        }

        // Recurring exceptions mapping requires subtasks/attachments lists (used by initial-sync
        // inlining in the legacy pull). For the change-feed payload we don't inline children —
        // they are emitted as their own SyncChange rows. Pass empty lists.
        private string SerializeRecurringException(RecurringTaskException entity)
        {
            var dto = entity.ToSyncDto(
                subtasks: Array.Empty<Models.RecurringSubtaskSyncItemDto>(),
                attachments: Array.Empty<Models.RecurringAttachmentSyncItemDto>());
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private readonly record struct DeletedPayload(Guid Id, DateTime DeletedAtUtc);
    }
}
