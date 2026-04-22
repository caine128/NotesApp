using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Attachments
{
    /// <summary>
    /// Integration tests for AttachmentRepository using a real SQL Server LocalDB database.
    ///
    /// Covers:
    /// - GetAllForTaskAsync: sorts by DisplayOrder, excludes deleted, isolates by user.
    /// - CountForTaskAsync: counts only non-deleted attachments.
    /// - GetChangedSinceAsync: initial (null) and incremental paths, user isolation,
    ///   soft-delete visibility, threshold boundary.
    /// - SoftDeleteAllForTaskAsync: bulk-deletes all non-deleted attachments for the task;
    ///   does not touch attachments for other tasks.
    /// - GetByIdUntrackedAsync: global query filter hides soft-deleted rows.
    /// - GetOrphanAttachmentsAsync: finds non-deleted attachments whose parent task is deleted.
    /// </summary>
    public sealed class AttachmentRepositoryTests
    {
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        private static async Task<(Guid userId, TaskItem task)> SeedUserAndTaskAsync(
            AppDbContext context, DateTime utcNow)
        {
            var userId = Guid.NewGuid();
            var taskResult = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Test task",
                null, null, null, null, null, null,
                TaskPriority.Normal, utcNow);
            taskResult.IsSuccess.Should().BeTrue();
            var task = taskResult.Value!;
            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return (userId, task);
        }

        private static async Task<Attachment> SeedAttachmentAsync(
            AppDbContext context,
            Guid userId,
            Guid taskId,
            int displayOrder,
            string fileName,
            DateTime utcNow)
        {
            var id = Guid.NewGuid();
            var result = Attachment.Create(
                id, userId, taskId,
                fileName, "application/pdf", 1024,
                $"{userId}/task-attachments/{taskId}/{id}/{fileName}",
                displayOrder, utcNow);
            result.IsSuccess.Should().BeTrue("test setup must produce a valid Attachment");
            var attachment = result.Value!;
            await context.Attachments.AddAsync(attachment);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return attachment;
        }

        private static async Task SoftDeleteAttachmentAsync(
            AppDbContext context, Attachment attachment, DateTime utcNow)
        {
            var tracked = await context.Attachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == attachment.Id);
            tracked.SoftDelete(utcNow);
            context.Attachments.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        // ── GetAllForTaskAsync ────────────────────────────────────────────────

        [Fact]
        public async Task GetAllForTaskAsync_returns_attachments_ordered_by_display_order()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            // Seed in reverse order to verify sorting
            var a3 = await SeedAttachmentAsync(context, userId, task.Id, 3, "c.pdf", _now);
            var a1 = await SeedAttachmentAsync(context, userId, task.Id, 1, "a.pdf", _now);
            var a2 = await SeedAttachmentAsync(context, userId, task.Id, 2, "b.pdf", _now);

            var result = await repo.GetAllForTaskAsync(task.Id, userId, CancellationToken.None);

            result.Should().HaveCount(3);
            result[0].Id.Should().Be(a1.Id);
            result[1].Id.Should().Be(a2.Id);
            result[2].Id.Should().Be(a3.Id);
        }

        [Fact]
        public async Task GetAllForTaskAsync_excludes_soft_deleted_attachments()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var live = await SeedAttachmentAsync(context, userId, task.Id, 1, "live.pdf", _now);
            var deleted = await SeedAttachmentAsync(context, userId, task.Id, 2, "deleted.pdf", _now);
            await SoftDeleteAttachmentAsync(context, deleted, _now.AddMinutes(1));

            var result = await repo.GetAllForTaskAsync(task.Id, userId, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(live.Id);
        }

        [Fact]
        public async Task GetAllForTaskAsync_isolates_attachments_by_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);

            var (userId, task) = await SeedUserAndTaskAsync(context, _now);
            var (otherUserId, otherTask) = await SeedUserAndTaskAsync(context, _now);

            var userAttachment = await SeedAttachmentAsync(context, userId, task.Id, 1, "mine.pdf", _now);
            await SeedAttachmentAsync(context, otherUserId, otherTask.Id, 1, "theirs.pdf", _now);

            var result = await repo.GetAllForTaskAsync(task.Id, userId, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(userAttachment.Id);
        }

        // ── CountForTaskAsync ─────────────────────────────────────────────────

        [Fact]
        public async Task CountForTaskAsync_returns_count_of_non_deleted_attachments()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            await SeedAttachmentAsync(context, userId, task.Id, 1, "a.pdf", _now);
            await SeedAttachmentAsync(context, userId, task.Id, 2, "b.pdf", _now);
            var toDelete = await SeedAttachmentAsync(context, userId, task.Id, 3, "c.pdf", _now);
            await SoftDeleteAttachmentAsync(context, toDelete, _now.AddMinutes(1));

            var count = await repo.CountForTaskAsync(task.Id, userId, CancellationToken.None);

            count.Should().Be(2, "soft-deleted attachment must not be counted");
        }

        [Fact]
        public async Task CountForTaskAsync_returns_zero_when_no_attachments()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var count = await repo.CountForTaskAsync(task.Id, userId, CancellationToken.None);

            count.Should().Be(0);
        }

        // ── GetChangedSinceAsync — initial sync ───────────────────────────────

        [Fact]
        public async Task GetChangedSinceAsync_initial_sync_returns_all_non_deleted_attachments()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var a1 = await SeedAttachmentAsync(context, userId, task.Id, 1, "a.pdf", _now);
            var a2 = await SeedAttachmentAsync(context, userId, task.Id, 2, "b.pdf", _now);
            var deleted = await SeedAttachmentAsync(context, userId, task.Id, 3, "c.pdf", _now);
            await SoftDeleteAttachmentAsync(context, deleted, _now.AddMinutes(1));

            var result = await repo.GetChangedSinceAsync(userId, null, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Select(a => a.Id).Should().BeEquivalentTo(new[] { a1.Id, a2.Id });
        }

        [Fact]
        public async Task GetChangedSinceAsync_initial_sync_isolates_by_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);

            var (userId, task) = await SeedUserAndTaskAsync(context, _now);
            var (otherUserId, otherTask) = await SeedUserAndTaskAsync(context, _now);

            var userAttachment = await SeedAttachmentAsync(context, userId, task.Id, 1, "mine.pdf", _now);
            await SeedAttachmentAsync(context, otherUserId, otherTask.Id, 1, "theirs.pdf", _now);

            var result = await repo.GetChangedSinceAsync(userId, null, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(userAttachment.Id);
        }

        // ── GetChangedSinceAsync — incremental sync ───────────────────────────

        [Fact]
        public async Task GetChangedSinceAsync_incremental_returns_only_attachments_updated_after_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var since = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var (userId, task) = await SeedUserAndTaskAsync(context, since.AddHours(-2));

            // Created before since — must NOT appear
            await SeedAttachmentAsync(context, userId, task.Id, 1, "old.pdf", since.AddHours(-1));

            // Created after since — must appear
            var recent = await SeedAttachmentAsync(context, userId, task.Id, 2, "new.pdf", since.AddHours(1));

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(recent.Id);
        }

        [Fact]
        public async Task GetChangedSinceAsync_incremental_includes_soft_deleted_attachments_updated_after_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var since = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var (userId, task) = await SeedUserAndTaskAsync(context, since.AddHours(-2));

            // Created before since, then soft-deleted after since
            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, "file.pdf", since.AddHours(-1));
            await SoftDeleteAttachmentAsync(context, attachment, since.AddHours(1));

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(attachment.Id);
            result[0].IsDeleted.Should().BeTrue("incremental sync must surface deleted attachments");
        }

        [Fact]
        public async Task GetChangedSinceAsync_incremental_excludes_attachments_not_changed_after_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var since = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var (userId, task) = await SeedUserAndTaskAsync(context, since.AddHours(-2));

            await SeedAttachmentAsync(context, userId, task.Id, 1, "stale.pdf", since.AddHours(-1));

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().BeEmpty();
        }

        // ── SoftDeleteAllForTaskAsync ─────────────────────────────────────────

        [Fact]
        public async Task SoftDeleteAllForTaskAsync_marks_all_non_deleted_task_attachments_as_deleted()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var a1 = await SeedAttachmentAsync(context, userId, task.Id, 1, "a.pdf", _now);
            var a2 = await SeedAttachmentAsync(context, userId, task.Id, 2, "b.pdf", _now);

            await repo.SoftDeleteAllForTaskAsync(task.Id, userId, _now.AddMinutes(5), CancellationToken.None);
            // SoftDeleteAllForTaskAsync uses the change-tracker pattern — caller must SaveChangesAsync.
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var all = await context.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.TaskId == task.Id)
                .ToListAsync();

            all.Should().HaveCount(2);
            all.Should().AllSatisfy(a => a.IsDeleted.Should().BeTrue());
        }

        [Fact]
        public async Task SoftDeleteAllForTaskAsync_does_not_touch_attachments_for_other_tasks()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);

            var (userId, task1) = await SeedUserAndTaskAsync(context, _now);
            var (_, task2) = await SeedUserAndTaskAsync(context, _now);
            // Make task2 belong to the same user
            var userId2 = task2.UserId;
            var otherAttachment = await SeedAttachmentAsync(context, userId2, task2.Id, 1, "other.pdf", _now);

            await SeedAttachmentAsync(context, userId, task1.Id, 1, "mine.pdf", _now);

            await repo.SoftDeleteAllForTaskAsync(task1.Id, userId, _now.AddMinutes(5), CancellationToken.None);
            // SoftDeleteAllForTaskAsync uses the change-tracker pattern — caller must SaveChangesAsync.
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var untouched = await context.Attachments
                .IgnoreQueryFilters()
                .FirstAsync(a => a.Id == otherAttachment.Id);

            untouched.IsDeleted.Should().BeFalse("SoftDeleteAllForTaskAsync must only affect the specified task");
        }

        // ── GetByIdUntrackedAsync — global query filter ───────────────────────

        [Fact]
        public async Task GetByIdUntrackedAsync_returns_null_for_soft_deleted_attachment()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, "file.pdf", _now);

            // Confirm visible before soft-delete
            var found = await repo.GetByIdUntrackedAsync(attachment.Id, CancellationToken.None);
            found.Should().NotBeNull();

            await SoftDeleteAttachmentAsync(context, attachment, _now.AddMinutes(1));

            // After soft-delete the global query filter must hide it
            var notFound = await repo.GetByIdUntrackedAsync(attachment.Id, CancellationToken.None);
            notFound.Should().BeNull("global query filter must hide soft-deleted attachments");
        }

        // ── GetOrphanAttachmentsAsync ─────────────────────────────────────────

        [Fact]
        public async Task GetOrphanAttachmentsAsync_returns_non_deleted_attachments_whose_parent_task_is_deleted()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var orphan = await SeedAttachmentAsync(context, userId, task.Id, 1, "orphan.pdf", _now);

            // Soft-delete the parent task directly in DB
            var trackedTask = await context.Tasks
                .IgnoreQueryFilters()
                .FirstAsync(t => t.Id == task.Id);
            trackedTask.SoftDelete(_now.AddMinutes(1));
            context.Tasks.Update(trackedTask);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var result = await repo.GetOrphanAttachmentsAsync(50, CancellationToken.None);

            result.Should().Contain(a => a.Id == orphan.Id,
                "attachment whose parent task is deleted is an orphan eligible for blob cleanup");
        }

        [Fact]
        public async Task GetOrphanAttachmentsAsync_does_not_return_already_deleted_attachments()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            var attachment = await SeedAttachmentAsync(context, userId, task.Id, 1, "file.pdf", _now);

            // Soft-delete BOTH the attachment and its parent task
            await SoftDeleteAttachmentAsync(context, attachment, _now.AddMinutes(1));

            var trackedTask = await context.Tasks
                .IgnoreQueryFilters()
                .FirstAsync(t => t.Id == task.Id);
            trackedTask.SoftDelete(_now.AddMinutes(1));
            context.Tasks.Update(trackedTask);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var result = await repo.GetOrphanAttachmentsAsync(50, CancellationToken.None);

            result.Should().NotContain(a => a.Id == attachment.Id,
                "already-deleted attachments are not orphans (blob was already scheduled for cleanup)");
        }

        [Fact]
        public async Task GetOrphanAttachmentsAsync_respects_limit_parameter()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new AttachmentRepository(context);
            var (userId, task) = await SeedUserAndTaskAsync(context, _now);

            // Seed 5 attachments
            for (int i = 1; i <= 5; i++)
                await SeedAttachmentAsync(context, userId, task.Id, i, $"file{i}.pdf", _now);

            // Soft-delete the parent task to make all 5 orphans
            var trackedTask = await context.Tasks
                .IgnoreQueryFilters()
                .FirstAsync(t => t.Id == task.Id);
            trackedTask.SoftDelete(_now.AddMinutes(1));
            context.Tasks.Update(trackedTask);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var result = await repo.GetOrphanAttachmentsAsync(limit: 3, CancellationToken.None);

            result.Should().HaveCount(3, "limit must be respected");
        }
    }
}
