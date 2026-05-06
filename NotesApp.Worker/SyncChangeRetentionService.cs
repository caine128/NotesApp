using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotesApp.Application.Common;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Worker.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Worker
{
    /// <summary>
    /// Periodic sweep that hard-deletes <c>SyncChanges</c> rows already acknowledged by every
    /// active device for the user, then advances the user's
    /// <c>SyncSequenceStates.MinRetainedSequence</c> watermark so the pull endpoint can detect
    /// stale cursors and signal re-bootstrap.
    ///
    /// Uses <c>ExecuteDeleteAsync</c> / <c>ExecuteUpdateAsync</c> for bulk operations — no change
    /// tracker materialization. Each user is processed independently so a single failure does not
    /// block other users.
    /// </summary>
    public sealed class SyncChangeRetentionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SyncRetentionOptions _options;
        private readonly ILogger<SyncChangeRetentionService> _logger;

        public SyncChangeRetentionService(IServiceScopeFactory scopeFactory,
                                          IOptions<SyncRetentionOptions> options,
                                          ILogger<SyncChangeRetentionService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SyncChangeRetentionService started. SweepInterval={Interval}, MinAgeForDelete={MinAge}",
                _options.SweepInterval, _options.MinAgeForDelete);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SyncChangeRetentionService sweep failed; will retry next tick.");
                }

                try
                {
                    await Task.Delay(_options.SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("SyncChangeRetentionService stopped.");
        }

        private async Task SweepOnceAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();

            var ageFloor = clock.UtcNow - _options.MinAgeForDelete;

            // One row per user — small set, full scan is fine.
            var users = await db.SyncSequenceStates
                .AsNoTracking()
                .Select(s => new { s.UserId, s.NextSequence })
                .ToListAsync(cancellationToken);

            var totalDeleted = 0;
            var usersTouched = 0;

            foreach (var user in users)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Min ack across active devices. Soft-deleted devices are excluded by the
                // UserDevices query filter. If the user has no active devices, MinAsync over an
                // empty set returns null and we skip the user — their rows stay until a device
                // comes back online.
                var minAck = await db.UserDevices
                    .Where(d => d.UserId == user.UserId && d.IsActive)
                    .Select(d => (long?)d.LastAckedSyncSequence)
                    .MinAsync(cancellationToken);

                if (minAck is null || minAck.Value <= 0)
                {
                    continue;
                }

                var pruneTo = minAck.Value;

                var deleted = await db.SyncChanges
                    .Where(x => x.UserId == user.UserId
                                && x.Sequence <= pruneTo
                                && x.ChangedAtUtc < ageFloor)
                    .ExecuteDeleteAsync(cancellationToken);

                if (deleted == 0)
                {
                    continue;
                }

                totalDeleted += deleted;
                usersTouched++;

                // Recompute MinRetainedSequence as the new MIN(Sequence) for the user, or
                // NextSequence if no rows remain (entire prefix is gone; cursor below NextSequence
                // is stale).
                var newMin = await db.SyncChanges
                    .Where(x => x.UserId == user.UserId)
                    .Select(x => (long?)x.Sequence)
                    .MinAsync(cancellationToken);

                var minRetained = newMin ?? user.NextSequence;

                await db.SyncSequenceStates
                    .Where(s => s.UserId == user.UserId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(s => s.MinRetainedSequence, minRetained),
                        cancellationToken);

                _logger.LogDebug(
                    "Retention swept user {UserId}: deleted={Deleted}, MinRetainedSequence={MinRetained}",
                    user.UserId, deleted, minRetained);
            }

            if (totalDeleted > 0)
            {
                _logger.LogInformation(
                    "Retention sweep complete: deleted {Total} SyncChange rows across {Users} users.",
                    totalDeleted, usersTouched);
            }
        }
    }
}
