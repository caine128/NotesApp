using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Integration tests for SyncPushCommandHandler with a focus on the RecurringTaskAttachment entity,
    /// using a real SQL Server LocalDB database.
    ///
    /// Covers:
    /// - Recurring attachment delete via sync push soft-deletes the record in DB.
    /// - Delete non-existent recurring attachment → NotFound status.
    /// - Delete already-deleted recurring attachment → NotFound (global query filter).
    ///
    /// Note: RecurringTaskAttachment *create* has no sync push path — files always go via the REST
    /// upload endpoint. The outbox propagates creation to all devices via the next sync pull.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class RecurringAttachmentSyncPushIntegrationTests
    {
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        private static async Task<(Guid userId, Guid deviceId)> SeedUserAndDeviceAsync(
            AppDbContext context, DateTime utcNow)
        {
            var user = User.Create($"test-{Guid.NewGuid()}@example.com", "Test User", utcNow).Value!;
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var device = UserDevice.Create(
                user.Id, $"token-{Guid.NewGuid()}", DevicePlatform.Android, "Test device", utcNow).Value!;
            await context.UserDevices.AddAsync(device);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            return (user.Id, device.Id);
        }

        private static SyncPushCommandHandler CreatePushHandler(
            AppDbContext context, Guid userId, DateTime utcNow)
        {
            var currentUserSvc = new Mock<ICurrentUserService>();
            currentUserSvc.Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                          .ReturnsAsync(userId);

            var clock = new Mock<ISystemClock>();
            clock.Setup(c => c.UtcNow).Returns(utcNow);

            return new SyncPushCommandHandler(
                currentUserSvc.Object,
                new TaskRepository(context, new Mock<IRecurrenceEngine>().Object),
                new NoteRepository(context),
                new BlockRepository(context),
                new UserDeviceRepository(context),
                new CategoryRepository(context),
                new SubtaskRepository(context),
                new AttachmentRepository(context),
                new RecurringTaskRootRepository(context),
                new RecurringTaskSeriesRepository(context),
                new RecurringTaskSubtaskRepository(context),
                new RecurringTaskExceptionRepository(context),
                new RecurringTaskAttachmentRepository(context),
                new OutboxRepository(context),
                new UnitOfWork(context),
                clock.Object,
                new Mock<ILogger<SyncPushCommandHandler>>().Object);
        }

        private static async Task<RecurringTaskSeries> SeedSeriesAsync(
            AppDbContext context, Guid userId, Guid rootId, DateTime utcNow)
        {
            var startsOn = DateOnly.FromDateTime(utcNow);
            var series = RecurringTaskSeries.Create(
                userId: userId,
                rootId: rootId,
                rruleString: "FREQ=DAILY",
                startsOnDate: startsOn,
                endsBeforeDate: null,
                title: "Test series",
                description: null,
                startTime: null,
                endTime: null,
                location: null,
                travelTime: null,
                categoryId: null,
                priority: TaskPriority.Normal,
                meetingLink: null,
                reminderOffsetMinutes: null,
                materializedUpToDate: startsOn.AddDays(-1),
                utcNow: utcNow).Value!;

            await context.RecurringTaskSeries.AddAsync(series);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return series;
        }

        private static async Task<RecurringTaskRoot> SeedRootAsync(
            AppDbContext context, Guid userId, DateTime utcNow)
        {
            var root = RecurringTaskRoot.Create(userId, utcNow).Value!;
            await context.RecurringTaskRoots.AddAsync(root);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return root;
        }

        private static async Task<RecurringTaskAttachment> SeedSeriesAttachmentAsync(
            AppDbContext context, Guid userId, Guid seriesId, int displayOrder, DateTime utcNow)
        {
            var id = Guid.NewGuid();
            var attachment = RecurringTaskAttachment.CreateForSeries(
                id, userId, seriesId,
                "file.pdf", "application/pdf", 1024,
                $"{userId}/recurring-series-attachments/{seriesId}/{id}/file.pdf",
                displayOrder, utcNow).Value!;

            await context.RecurringTaskAttachments.AddAsync(attachment);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return attachment;
        }

        // ── Recurring Attachment Delete ────────────────────────────────────────

        [Fact]
        public async Task Handle_RecurringAttachmentDelete_soft_deletes_attachment_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var root = await SeedRootAsync(context, userId, _now.AddHours(-1));
            var series = await SeedSeriesAsync(context, userId, root.Id, _now.AddHours(-1));
            var attachment = await SeedSeriesAttachmentAsync(context, userId, series.Id, 1, _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = new[] { new RecurringAttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringAttachments.Deleted.Should().ContainSingle(r =>
                r.Id == attachment.Id && r.Status == SyncPushDeletedStatus.Deleted);

            var dbAttachment = await context.RecurringTaskAttachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            dbAttachment.IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_RecurringAttachmentDelete_emits_outbox_message()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var root = await SeedRootAsync(context, userId, _now.AddHours(-1));
            var series = await SeedSeriesAsync(context, userId, root.Id, _now.AddHours(-1));
            var attachment = await SeedSeriesAttachmentAsync(context, userId, series.Id, 1, _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            await handler.Handle(new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = new[] { new RecurringAttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            }, CancellationToken.None);

            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().ContainSingle(m => m.AggregateType == nameof(RecurringTaskAttachment));
        }

        [Fact]
        public async Task Handle_RecurringAttachmentDelete_non_existent_attachment_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = new[] { new RecurringAttachmentDeletedPushItemDto { Id = Guid.NewGuid() } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringAttachments.Deleted.Should().ContainSingle(r =>
                r.Status == SyncPushDeletedStatus.NotFound);
        }

        [Fact]
        public async Task Handle_RecurringAttachmentDelete_already_deleted_returns_not_found_via_global_filter()
        {
            // The global query filter hides soft-deleted attachments.
            // GetByIdUntrackedAsync returns null → NotFound (idempotent for the client).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var root = await SeedRootAsync(context, userId, _now.AddHours(-1));
            var series = await SeedSeriesAsync(context, userId, root.Id, _now.AddHours(-1));
            var attachment = await SeedSeriesAttachmentAsync(context, userId, series.Id, 1, _now.AddHours(-1));

            // Soft-delete the attachment directly
            var tracked = await context.RecurringTaskAttachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            tracked.SoftDelete(_now.AddMinutes(-5));
            context.RecurringTaskAttachments.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                RecurringAttachments = new SyncPushRecurringAttachmentsDto
                {
                    Deleted = new[] { new RecurringAttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringAttachments.Deleted.Should().ContainSingle(r =>
                r.Id == attachment.Id && r.Status == SyncPushDeletedStatus.NotFound,
                "global query filter makes deleted attachments invisible → NotFound");
        }
    }
}
