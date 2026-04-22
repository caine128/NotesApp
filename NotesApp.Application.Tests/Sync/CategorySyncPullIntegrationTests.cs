using NotesApp.Application.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Categories.Commands.DeleteTaskCategory;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Sync.Queries;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Integration tests for GetSyncChangesQueryHandler (sync pull) with a focus on the
    /// category entity, using a real SQL Server database.
    ///
    /// Covers:
    /// - Initial sync: all non-deleted categories appear in the "created" bucket.
    /// - Initial sync: soft-deleted categories are hidden (global query filter).
    /// - Initial sync: user isolation (other users' categories are invisible).
    /// - Incremental sync: correct bucketing (created / updated / deleted).
    /// - Incremental sync: categories not changed after sinceUtc are excluded.
    /// - End-to-end cross-entity test: after a REST category delete, sync pull shows
    ///   the category in the "deleted" bucket AND the affected task in the "updated"
    ///   bucket (with its CategoryId nulled and Version incremented).
    /// </summary>
    public sealed class CategorySyncPullIntegrationTests
    {
        private static GetSyncChangesQueryHandler CreatePullHandler(
            AppDbContext context,
            Guid userId)
        {
            var currentUserSvc = new Mock<ICurrentUserService>();
            currentUserSvc.Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                          .ReturnsAsync(userId);

            return new GetSyncChangesQueryHandler(
                new TaskRepository(context, new Mock<IRecurrenceEngine>().Object),
                new NoteRepository(context),
                new BlockRepository(context),
                new AssetRepository(context),
                new UserDeviceRepository(context),
                new CategoryRepository(context),
                new SubtaskRepository(context),
                new AttachmentRepository(context),
                // REFACTORED: added recurring-task repos for recurring-tasks feature
                new RecurringTaskRootRepository(context),
                new RecurringTaskSeriesRepository(context),
                new RecurringTaskSubtaskRepository(context),
                new RecurringTaskExceptionRepository(context),
                currentUserSvc.Object,
                new Mock<ILogger<GetSyncChangesQueryHandler>>().Object);
        }

        private static DeleteTaskCategoryCommandHandler CreateDeleteHandler(
            AppDbContext context,
            Guid userId,
            DateTime utcNow)
        {
            var currentUserSvc = new Mock<ICurrentUserService>();
            currentUserSvc.Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                          .ReturnsAsync(userId);

            var clock = new Mock<ISystemClock>();
            clock.Setup(c => c.UtcNow).Returns(utcNow);

            return new DeleteTaskCategoryCommandHandler(
                new CategoryRepository(context),
                new TaskRepository(context, new Mock<IRecurrenceEngine>().Object),
                new OutboxRepository(context),
                new UnitOfWork(context),
                currentUserSvc.Object,
                clock.Object,
                new Mock<ILogger<DeleteTaskCategoryCommandHandler>>().Object);
        }

        private static async Task<TaskCategory> SeedCategoryAsync(
            AppDbContext context,
            Guid userId,
            string name,
            DateTime utcNow)
        {
            var cat = TaskCategory.Create(userId, name, utcNow).Value!;
            await context.TaskCategories.AddAsync(cat);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return cat;
        }

        private static async Task<TaskItem> SeedTaskAsync(
            AppDbContext context,
            Guid userId,
            Guid? categoryId,
            DateTime utcNow)
        {
            var task = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Task",
                null, null, null, null, null,
                categoryId, TaskPriority.Normal, utcNow).Value!;
            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return task;
        }

        private static async Task SoftDeleteCategoryInDbAsync(
            AppDbContext context,
            TaskCategory category,
            DateTime utcNow)
        {
            var tracked = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            tracked.SoftDelete(utcNow);
            context.TaskCategories.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        // -----------------------------------------------------------------------
        // Initial sync (sinceUtc = null)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_returns_all_non_deleted_categories_as_created()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var cat1 = await SeedCategoryAsync(context, userId, "Work", now);
            var cat2 = await SeedCategoryAsync(context, userId, "Personal", now);

            var handler = CreatePullHandler(context, userId);
            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Categories.Created.Select(c => c.Id)
                  .Should().BeEquivalentTo(new[] { cat1.Id, cat2.Id });
            result.Value.Categories.Updated.Should().BeEmpty();
            result.Value.Categories.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_initial_sync_excludes_soft_deleted_categories()
        {
            // Global query filter hides soft-deleted categories from initial sync.
            // The client that deleted the category is already in sync; new clients
            // just won't see it.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var liveCategory = await SeedCategoryAsync(context, userId, "Work", now);
            var deletedCategory = await SeedCategoryAsync(context, userId, "Gone", now);

            await SoftDeleteCategoryInDbAsync(context, deletedCategory, now.AddMinutes(1));

            var handler = CreatePullHandler(context, userId);
            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Categories.Created.Should().ContainSingle(c => c.Id == liveCategory.Id);
            result.Value.Categories.Created.Should().NotContain(c => c.Id == deletedCategory.Id);
            result.Value.Categories.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_initial_sync_isolates_categories_by_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var myCategory = await SeedCategoryAsync(context, userId, "Work", now);
            await SeedCategoryAsync(context, otherUserId, "Other", now);

            var handler = CreatePullHandler(context, userId);
            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Categories.Created.Should().ContainSingle(c => c.Id == myCategory.Id);
        }

        // -----------------------------------------------------------------------
        // Incremental sync (sinceUtc set)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_incremental_sync_buckets_categories_into_created_updated_deleted()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Created AFTER since → "created" bucket.
            var newCategory = await SeedCategoryAsync(context, userId, "New", since.AddHours(1));

            // Created BEFORE since, NOT deleted → "updated" bucket.
            var updatedCategory = await SeedCategoryAsync(context, userId, "Updated", since.AddHours(-2));
            // Manually bump UpdatedAtUtc to after since to simulate a rename.
            await context.TaskCategories
                .Where(c => c.Id == updatedCategory.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAtUtc, since.AddHours(2)));

            // Created BEFORE since, then soft-deleted AFTER since → "deleted" bucket.
            var deletedCategory = await SeedCategoryAsync(context, userId, "Deleted", since.AddHours(-3));
            await SoftDeleteCategoryInDbAsync(context, deletedCategory, since.AddHours(3));

            var handler = CreatePullHandler(context, userId);
            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            var cats = result.Value.Categories;

            cats.Created.Should().ContainSingle(c => c.Id == newCategory.Id);
            cats.Updated.Should().ContainSingle(c => c.Id == updatedCategory.Id);
            cats.Deleted.Should().ContainSingle(c => c.Id == deletedCategory.Id);
            cats.Deleted[0].DeletedAtUtc.Should().BeCloseTo(since.AddHours(3), precision: TimeSpan.FromMilliseconds(10));
        }

        [Fact]
        public async Task Handle_incremental_sync_excludes_categories_unchanged_before_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Created two hours before since — has never been modified since.
            await SeedCategoryAsync(context, userId, "Stale", since.AddHours(-2));

            var handler = CreatePullHandler(context, userId);
            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Categories.Created.Should().BeEmpty();
            result.Value.Categories.Updated.Should().BeEmpty();
            result.Value.Categories.Deleted.Should().BeEmpty();
        }

        // -----------------------------------------------------------------------
        // End-to-end cross-entity: REST delete + sync pull
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_after_rest_delete_sync_pull_sees_deleted_category_and_updated_task()
        {
            // This test validates the full REST-delete → sync-pull chain:
            //
            // 1. A category and an associated task exist in the DB.
            // 2. The REST DELETE handler is called (DeleteTaskCategoryCommandHandler):
            //    - Soft-deletes the category (UpdatedAtUtc = deleteTime).
            //    - Bulk-nulls CategoryId on affected tasks via ClearCategoryFromTasksAsync
            //      (sets task.UpdatedAtUtc = deleteTime, task.Version++).
            // 3. The next sync pull (sinceUtc = priorSyncTime < deleteTime) must return:
            //    - Category in the "deleted" bucket (UpdatedAtUtc > sinceUtc, IsDeleted = true).
            //    - Task in the "updated" bucket (UpdatedAtUtc > sinceUtc, CategoryId = null).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var priorSyncTime = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var createdTime = priorSyncTime.AddHours(-1); // entities existed before last sync
            var deleteTime = priorSyncTime.AddHours(2);   // delete happens after last sync

            // 1. Seed category and task (both created before the prior sync).
            var category = await SeedCategoryAsync(context, userId, "Work", createdTime);
            var task = await SeedTaskAsync(context, userId, category.Id, createdTime);
            var taskVersionBefore = task.Version;

            // 2. Call the REST delete handler at deleteTime.
            var deleteHandler = CreateDeleteHandler(context, userId, deleteTime);
            var deleteResult = await deleteHandler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);
            deleteResult.IsSuccess.Should().BeTrue();

            // 3. Run sync pull from priorSyncTime.
            var pullHandler = CreatePullHandler(context, userId);
            var pullResult = await pullHandler.Handle(
                new GetSyncChangesQuery(SinceUtc: priorSyncTime, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            pullResult.IsSuccess.Should().BeTrue();
            var syncDto = pullResult.Value;

            // Category must appear in "deleted" bucket.
            syncDto.Categories.Deleted.Should().ContainSingle(c => c.Id == category.Id,
                "the REST-deleted category must be visible in the deleted bucket");
            syncDto.Categories.Created.Should().NotContain(c => c.Id == category.Id);
            syncDto.Categories.Updated.Should().NotContain(c => c.Id == category.Id);

            // Task must appear in "updated" bucket (CategoryId was cleared, Version was bumped).
            syncDto.Tasks.Updated.Should().ContainSingle(t => t.Id == task.Id,
                "the task whose CategoryId was cleared must appear in the updated bucket");
            syncDto.Tasks.Deleted.Should().NotContain(t => t.Id == task.Id);

            // Verify task DTO reflects the cleared FK and incremented version.
            var taskDto = syncDto.Tasks.Updated.Single(t => t.Id == task.Id);
            taskDto.CategoryId.Should().BeNull("ClearCategoryFromTasksAsync must have nulled the FK");
            taskDto.Version.Should().Be(taskVersionBefore + 1,
                "ClearCategoryFromTasksAsync must have incremented the version");
        }
    }
}
