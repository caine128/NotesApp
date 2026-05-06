using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.RecurringAttachments;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Bootstrap snapshot for new or re-bootstrapping devices. Captures the current
    /// <c>MAX(SyncChange.Sequence)</c> as <c>BootstrapSequence</c> first, then reads all
    /// non-deleted entities for the user via existing <c>GetChangedSinceAsync(userId, null, ct)</c>
    /// (semantics: when sinceUtc is null, returns all non-deleted entities).
    ///
    /// Any change that lands during the entity reads will appear in the next <c>GET /api/sync/pull</c>
    /// at <c>afterSequence = BootstrapSequence</c> and be applied as an idempotent overwrite.
    /// </summary>
    public sealed class GetSyncSnapshotQueryHandler
        : IRequestHandler<GetSyncSnapshotQuery, Result<SyncSnapshotDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly IAttachmentRepository _attachmentRepository;
        private readonly IRecurringTaskRootRepository _rootRepository;
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ISyncChangeRepository _syncChangeRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GetSyncSnapshotQueryHandler> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public GetSyncSnapshotQueryHandler(ITaskRepository taskRepository,
                                           INoteRepository noteRepository,
                                           IBlockRepository blockRepository,
                                           IAssetRepository assetRepository,
                                           ICategoryRepository categoryRepository,
                                           ISubtaskRepository subtaskRepository,
                                           IAttachmentRepository attachmentRepository,
                                           IRecurringTaskRootRepository rootRepository,
                                           IRecurringTaskSeriesRepository seriesRepository,
                                           IRecurringTaskSubtaskRepository recurringSubtaskRepository,
                                           IRecurringTaskExceptionRepository exceptionRepository,
                                           IRecurringTaskAttachmentRepository recurringAttachmentRepository,
                                           IUserDeviceRepository deviceRepository,
                                           ISyncChangeRepository syncChangeRepository,
                                           ICurrentUserService currentUserService,
                                           ISystemClock clock,
                                           IUnitOfWork unitOfWork,
                                           ILogger<GetSyncSnapshotQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;
            _assetRepository = assetRepository;
            _categoryRepository = categoryRepository;
            _subtaskRepository = subtaskRepository;
            _attachmentRepository = attachmentRepository;
            _rootRepository = rootRepository;
            _seriesRepository = seriesRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _exceptionRepository = exceptionRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository;
            _deviceRepository = deviceRepository;
            _syncChangeRepository = syncChangeRepository;
            _currentUserService = currentUserService;
            _clock = clock;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<SyncSnapshotDto>> Handle(GetSyncSnapshotQuery request, CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Validate device ownership when provided.
            Domain.Users.UserDevice? device = null;
            if (request.DeviceId is Guid deviceId)
            {
                device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
                if (device is null || device.UserId != userId || !device.IsActive)
                {
                    return Result.Fail(new Error("Device.NotFound"));
                }
            }

            // Capture BootstrapSequence BEFORE the entity reads. Any change committed during the
            // reads will have Sequence > bootstrapSequence and surface on the next /pull call,
            // applied as idempotent overwrite.
            var bootstrapSequence = await _syncChangeRepository.GetCurrentMaxSequenceAsync(userId, cancellationToken);

            // Load current non-deleted entities for every family. Reusing GetChangedSinceAsync's
            // sinceUtc=null semantics (returns all non-deleted for the user). When the legacy
            // pull is removed in cutover, these calls remain the only consumers — keep the
            // method on the repository contracts even after old pull deletion.
            var tasks = await _taskRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var notes = await _noteRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var blocks = await _blockRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var assets = await _assetRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var categories = await _categoryRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var subtasks = await _subtaskRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var attachments = await _attachmentRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var roots = await _rootRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var allSeries = await _seriesRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var recurringSubtasks = await _recurringSubtaskRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var exceptions = await _exceptionRepository.GetChangedSinceAsync(userId, null, cancellationToken);
            var recurringAttachments = await _recurringAttachmentRepository.GetChangedSinceAsync(userId, null, cancellationToken);

            var items = new List<SyncSnapshotItemDto>(
                tasks.Count + notes.Count + blocks.Count + assets.Count +
                categories.Count + subtasks.Count + attachments.Count +
                roots.Count + allSeries.Count + recurringSubtasks.Count +
                exceptions.Count + recurringAttachments.Count);

            foreach (var t in tasks)
                items.Add(MakeItem(SyncEntityFamily.Task, t.Id, JsonSerializer.Serialize(t.ToSyncDto(), JsonOptions)));
            foreach (var n in notes)
                items.Add(MakeItem(SyncEntityFamily.Note, n.Id, JsonSerializer.Serialize(n.ToSyncDto(), JsonOptions)));
            foreach (var b in blocks)
                items.Add(MakeItem(SyncEntityFamily.Block, b.Id, JsonSerializer.Serialize(b.ToSyncDto(), JsonOptions)));
            foreach (var a in assets)
                items.Add(MakeItem(SyncEntityFamily.Asset, a.Id, JsonSerializer.Serialize(a.ToSyncDto(), JsonOptions)));
            foreach (var c in categories)
                items.Add(MakeItem(SyncEntityFamily.Category, c.Id, JsonSerializer.Serialize(c.ToSyncDto(), JsonOptions)));
            foreach (var s in subtasks)
                items.Add(MakeItem(SyncEntityFamily.Subtask, s.Id, JsonSerializer.Serialize(s.ToSyncDto(), JsonOptions)));
            foreach (var att in attachments)
                items.Add(MakeItem(SyncEntityFamily.Attachment, att.Id, JsonSerializer.Serialize(att.ToSyncDto(), JsonOptions)));
            foreach (var r in roots)
                items.Add(MakeItem(SyncEntityFamily.RecurringTaskRoot, r.Id, JsonSerializer.Serialize(r.ToSyncDto(), JsonOptions)));
            foreach (var s in allSeries)
                items.Add(MakeItem(SyncEntityFamily.RecurringTaskSeries, s.Id, JsonSerializer.Serialize(s.ToSyncDto(), JsonOptions)));
            foreach (var rs in recurringSubtasks)
                items.Add(MakeItem(SyncEntityFamily.RecurringTaskSubtask, rs.Id, JsonSerializer.Serialize(rs.ToSyncDto(), JsonOptions)));
            foreach (var ex in exceptions)
            {
                // Per-exception subtasks/attachments are emitted as their own snapshot rows; pass
                // empty inline lists for the exception's own ToSyncDto.
                var dto = ex.ToSyncDto(
                    subtasks: Array.Empty<RecurringSubtaskSyncItemDto>(),
                    attachments: Array.Empty<RecurringAttachmentSyncItemDto>());
                items.Add(MakeItem(SyncEntityFamily.RecurringTaskException, ex.Id, JsonSerializer.Serialize(dto, JsonOptions)));
            }
            foreach (var ra in recurringAttachments)
                items.Add(MakeItem(SyncEntityFamily.RecurringTaskAttachment, ra.Id, JsonSerializer.Serialize(ra.ToSyncDto(), JsonOptions)));

            // Snap the device's acked-sequence high-water mark to bootstrapSequence so retention
            // counts this device as caught up to that point.
            if (device is not null)
            {
                device.AdvanceLastAckedSyncSequence(bootstrapSequence, _clock.UtcNow);
                _deviceRepository.Update(device);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Sync snapshot served for user {UserId}: {Count} items, BootstrapSequence={BootstrapSequence}",
                userId, items.Count, bootstrapSequence);

            return Result.Ok(new SyncSnapshotDto(items, bootstrapSequence));
        }

        private static SyncSnapshotItemDto MakeItem(SyncEntityFamily family, Guid id, string payloadJson)
        {
            JsonElement payload;
            using (var doc = JsonDocument.Parse(payloadJson))
            {
                payload = doc.RootElement.Clone();
            }
            return new SyncSnapshotItemDto(family, id, payload);
        }
    }
}
