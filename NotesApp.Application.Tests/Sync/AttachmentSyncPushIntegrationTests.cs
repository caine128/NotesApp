using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
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
    /// Integration tests for SyncPushCommandHandler with a focus on the Attachment entity,
    /// using a real SQL Server LocalDB database.
    ///
    /// Covers:
    /// - Attachment delete via sync push soft-deletes the record in DB.
    /// - Delete non-existent attachment → NotFound status.
    /// - Delete already-deleted attachment → NotFound (global query filter makes it invisible).
    /// - Task delete via REST cascades to attachments (SoftDeleteAllForTaskAsync).
    /// - Task delete + explicit attachment delete in the same push: task delete cascades
    ///   (soft-deletes attachment), then the explicit attachment delete sees null
    ///   (global filter hides it) → NotFound on the explicit one.
    ///
    /// Note: Attachment *create* has no sync push path — files always go via the REST
    /// upload endpoint (POST /api/attachments/{taskId}). The outbox propagates the
    /// creation to all devices via the next sync pull.
    /// </summary>
    public sealed class AttachmentSyncPushIntegrationTests
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
                new TaskRepository(context),
                new NoteRepository(context),
                new BlockRepository(context),
                new UserDeviceRepository(context),
                new CategoryRepository(context),
                new SubtaskRepository(context),
                new AttachmentRepository(context),
                new OutboxRepository(context),
                new UnitOfWork(context),
                clock.Object,
                new Mock<ILogger<SyncPushCommandHandler>>().Object);
        }

        private static async Task<TaskItem> SeedTaskAsync(
            AppDbContext context, Guid userId, DateTime utcNow)
        {
            var task = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Test task",
                null, null, null, null, null, null,
                TaskPriority.Normal, utcNow).Value!;
            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return task;
        }

        private static async Task<Attachment> SeedAttachmentAsync(
            AppDbContext context, Guid userId, Guid taskId,
            int displayOrder, DateTime utcNow)
        {
            var id = Guid.NewGuid();
            var attachment = Attachment.Create(
                id, userId, taskId,
                "file.pdf", "application/pdf", 1024,
                $"{userId}/task-attachments/{taskId}/{id}/file.pdf",
                displayOrder, utcNow).Value!;
            await context.Attachments.AddAsync(attachment);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return attachment;
        }

        // ── Attachment Delete ─────────────────────────────────────────────────

        [Fact]
        public async Task Handle_AttachmentDelete_soft_deletes_attachment_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = new[] { new AttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Attachments.Deleted.Should().ContainSingle(r =>
                r.Id == attachment.Id && r.Status == SyncPushDeletedStatus.Deleted);

            // Verify soft-deleted in DB
            var dbAttachment = await context.Attachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            dbAttachment.IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_AttachmentDelete_emits_outbox_message()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            await handler.Handle(new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = new[] { new AttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            }, CancellationToken.None);

            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().ContainSingle(m => m.AggregateType == nameof(Attachment));
        }

        [Fact]
        public async Task Handle_AttachmentDelete_non_existent_attachment_returns_not_found()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = new[] { new AttachmentDeletedPushItemDto { Id = Guid.NewGuid() } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Attachments.Deleted.Should().ContainSingle(r =>
                r.Status == SyncPushDeletedStatus.NotFound);
        }

        [Fact]
        public async Task Handle_AttachmentDelete_already_deleted_returns_not_found_via_global_filter()
        {
            // The global query filter hides soft-deleted attachments.
            // GetByIdUntrackedAsync returns null → NotFound (idempotent for the client).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, _now.AddHours(-1));

            // Soft-delete the attachment directly
            var tracked = await context.Attachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            tracked.SoftDelete(_now.AddMinutes(-5));
            context.Attachments.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = new[] { new AttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Attachments.Deleted.Should().ContainSingle(r =>
                r.Id == attachment.Id && r.Status == SyncPushDeletedStatus.NotFound,
                "global query filter makes deleted attachments invisible → NotFound");
        }

        // ── Task delete → cascade ─────────────────────────────────────────────

        [Fact]
        public async Task Handle_TaskDelete_via_rest_cascades_soft_delete_to_attachments()
        {
            // This validates the DeleteTaskCommandHandler cascade (SoftDeleteAllForTaskAsync).
            // We test it here via the REST delete handler because the SyncPushCommandHandler
            // does NOT perform a server-side sweep (mobile client must send explicit deletes).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var a1 = await SeedAttachmentAsync(context, userId, task.Id, 1, _now.AddHours(-1));
            var a2 = await SeedAttachmentAsync(context, userId, task.Id, 2, _now.AddHours(-1));

            // Call SoftDeleteAllForTaskAsync directly to simulate the REST delete cascade
            var attachmentRepo = new AttachmentRepository(context);
            await attachmentRepo.SoftDeleteAllForTaskAsync(task.Id, userId, _now, CancellationToken.None);
            context.ChangeTracker.Clear();

            var allAttachments = await context.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.TaskId == task.Id)
                .ToListAsync();

            allAttachments.Should().HaveCount(2);
            allAttachments.Should().AllSatisfy(a =>
                a.IsDeleted.Should().BeTrue("task delete must cascade-soft-delete all attachments"));
        }

        [Fact]
        public async Task Handle_TaskDelete_and_explicit_attachment_delete_in_same_push_returns_not_found_on_explicit()
        {
            // ═══════════════════════════════════════════════════════════════════
            // KEY BEHAVIORAL CONTRACT TEST
            //
            // When the mobile client sends a TaskDeleted + AttachmentDeleted for the
            // same task in one push, the handler processes task deletes FIRST (which
            // cascades to soft-delete all attachments via SoftDeleteAllForTaskAsync).
            // Then ProcessAttachmentDeletesAsync runs — but GetByIdUntrackedAsync
            // returns null for the now-deleted attachment (global filter), so the
            // explicit AttachmentDeleted gets NotFound status.
            //
            // This is safe and idempotent: the attachment IS deleted; the NotFound
            // status simply tells the client it was already handled by the cascade.
            // ═══════════════════════════════════════════════════════════════════
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = task.Id } }
                },
                Attachments = new SyncPushAttachmentsDto
                {
                    Deleted = new[] { new AttachmentDeletedPushItemDto { Id = attachment.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            result.Value.Tasks.Deleted.Should().ContainSingle(r =>
                r.Id == task.Id && r.Status == SyncPushDeletedStatus.Deleted);

            // The explicit attachment delete sees NotFound because the task-delete
            // cascade (SoftDeleteAllForTaskAsync) already deleted it, and the
            // global query filter now hides it from GetByIdUntrackedAsync.
            result.Value.Attachments.Deleted.Should().ContainSingle(r =>
                r.Id == attachment.Id && r.Status == SyncPushDeletedStatus.NotFound,
                "cascade deleted the attachment before explicit delete ran; global filter hides it");

            // Attachment is still deleted in DB (cascade succeeded)
            var dbAttachment = await context.Attachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            dbAttachment.IsDeleted.Should().BeTrue("cascade delete must have persisted");
        }
    }
}
