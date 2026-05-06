using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Application.Sync.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Sequence-based sync pull. Replaces <see cref="GetSyncChangesQueryHandler"/>.
    /// </summary>
    public sealed class GetSyncPullQueryHandler
        : IRequestHandler<GetSyncPullQuery, Result<SyncPullDto>>
    {
        private readonly ISyncChangeRepository _syncChangeRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<GetSyncPullQueryHandler> _logger;

        public GetSyncPullQueryHandler(ISyncChangeRepository syncChangeRepository,
                                       IUserDeviceRepository deviceRepository,
                                       ICurrentUserService currentUserService,
                                       ISystemClock clock,
                                       IUnitOfWork unitOfWork,
                                       ILogger<GetSyncPullQueryHandler> logger)
        {
            _syncChangeRepository = syncChangeRepository;
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
            _clock = clock;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<SyncPullDto>> Handle(GetSyncPullQuery request, CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var effectiveLimit = request.Limit ?? SyncPullLimits.DefaultPullLimit;

            // Validate device ownership/active status when provided.
            // GetByIdAsync respects the soft-delete query filter (deleted device → null).
            Domain.Users.UserDevice? device = null;
            if (request.DeviceId is Guid deviceId)
            {
                device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
                if (device is null || device.UserId != userId || !device.IsActive)
                {
                    return Result.Fail(new Error("Device.NotFound"));
                }
            }

            // Stale-cursor check: if the requested AfterSequence has been pruned by the retention
            // sweep, the client must re-bootstrap via /api/sync/snapshot.
            if (request.AfterSequence > 0)
            {
                var minRetained = await _syncChangeRepository.GetMinRetainedSequenceAsync(userId, cancellationToken);
                if (request.AfterSequence < minRetained)
                {
                    return Result.Fail(SyncErrors.CursorStale(request.AfterSequence, minRetained));
                }
            }

            // Seek + 1 to detect HasMore.
            var rows = await _syncChangeRepository.GetAfterSequenceAsync(
                userId, request.AfterSequence, effectiveLimit + 1, cancellationToken);

            var hasMore = rows.Count > effectiveLimit;
            var page = hasMore ? rows.Take(effectiveLimit).ToList() : rows.ToList();

            var items = new List<SyncPullItemDto>(page.Count);
            foreach (var row in page)
            {
                JsonElement payload;
                using (var doc = JsonDocument.Parse(row.PayloadJson))
                {
                    // Clone() detaches the JsonElement from the disposed JsonDocument so it remains
                    // safe to serialize after `using` exits.
                    payload = doc.RootElement.Clone();
                }

                items.Add(new SyncPullItemDto(
                    row.Sequence,
                    row.EntityFamily,
                    row.Operation,
                    row.EntityId,
                    row.ChangedAtUtc,
                    row.OriginDeviceId,
                    payload));
            }

            var nextSequence = page.Count == 0 ? request.AfterSequence : page[^1].Sequence;

            // Advance the device's acked-sequence high-water mark. A client requesting
            // afterSequence=N implies it has applied through N. Idempotent and monotonic.
            if (device is not null && request.AfterSequence > device.LastAckedSyncSequence)
            {
                device.AdvanceLastAckedSyncSequence(request.AfterSequence, _clock.UtcNow);
                _deviceRepository.Update(device);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Sync pull served for user {UserId}: returned {Count} changes (HasMore={HasMore}), NextSequence={NextSequence}",
                userId, items.Count, hasMore, nextSequence);

            return Result.Ok(new SyncPullDto(items, nextSequence, hasMore));
        }
    }
}
