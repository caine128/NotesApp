using NotesApp.Application.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Categories.Commands.DeleteTaskCategory;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Categories
{
    /// <summary>
    /// Integration tests for DeleteTaskCategoryCommandHandler that hit a real SQL Server database.
    ///
    /// Key behaviors verified:
    /// 1. Soft-deletes the category row (IsDeleted = true in DB).
    /// 2. Clears CategoryId FK on affected tasks and increments their Version.
    /// 3. Does NOT touch tasks belonging to other users.
    /// 4. Does NOT touch tasks assigned to a different category.
    /// 5. Does NOT touch tasks that are already soft-deleted.
    /// 6. Idempotency: if the category is already gone, still clears residual task refs and returns OK.
    /// 7. Ownership guard: returns NotFound and leaves the DB untouched for foreign categories.
    /// 8. Emits an OutboxMessage on successful delete.
    ///
    /// Note: The REST delete handler calls ClearCategoryFromTasksAsync server-side, which
    /// uses ExecuteUpdateAsync.  Because this bypasses the EF change tracker, affected
    /// tasks must be reloaded from the DB to verify the update.
    /// </summary>
    public sealed class DeleteTaskCategoryIntegrationTests
    {
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        private DeleteTaskCategoryCommandHandler CreateHandler(
            AppDbContext context,
            Guid currentUserId)
        {
            var currentUserSvc = new Mock<ICurrentUserService>();
            currentUserSvc.Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                          .ReturnsAsync(currentUserId);

            var clockMock = new Mock<ISystemClock>();
            clockMock.Setup(c => c.UtcNow).Returns(_now);

            return new DeleteTaskCategoryCommandHandler(
                new CategoryRepository(context),
                new TaskRepository(context, new Mock<IRecurrenceEngine>().Object),
                new OutboxRepository(context),
                new UnitOfWork(context),
                currentUserSvc.Object,
                clockMock.Object,
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
                userId,
                new DateOnly(2025, 6, 1),
                "Test task",
                null, null, null, null, null,
                categoryId,
                TaskPriority.Normal,
                utcNow).Value!;

            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return task;
        }

        // -----------------------------------------------------------------------
        // Core soft-delete and FK clearing
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_soft_deletes_category_and_nulls_task_category_fk()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));
            var task = await SeedTaskAsync(context, userId, category.Id, _now.AddHours(-1));

            var handler = CreateHandler(context, userId);
            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            // Category must be soft-deleted in the DB.
            var dbCategory = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            dbCategory.IsDeleted.Should().BeTrue();

            // Task FK must be nulled out.
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == task.Id);
            dbTask.CategoryId.Should().BeNull("ClearCategoryFromTasksAsync must null the FK");
        }

        [Fact]
        public async Task Handle_increments_task_version_when_clearing_category_fk()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));
            var task = await SeedTaskAsync(context, userId, category.Id, _now.AddHours(-1));

            var versionBefore = task.Version;

            var handler = CreateHandler(context, userId);
            await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            // ClearCategoryFromTasksAsync does: Version = Version + 1.
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == task.Id);
            dbTask.Version.Should().Be(versionBefore + 1,
                "version must be incremented so stale mobile push attempts receive VersionMismatch");
        }

        [Fact]
        public async Task Handle_updates_task_updatedAtUtc_when_clearing_category_fk()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-2));
            var task = await SeedTaskAsync(context, userId, category.Id, _now.AddHours(-2));

            var handler = CreateHandler(context, userId);
            await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            var dbTask = await context.Tasks.FirstAsync(t => t.Id == task.Id);
            // The mock clock returns _now, so UpdatedAtUtc must equal _now.
            dbTask.UpdatedAtUtc.Should().Be(_now,
                "ClearCategoryFromTasksAsync sets UpdatedAtUtc to utcNow from the clock");
        }

        // -----------------------------------------------------------------------
        // Isolation: tasks that must NOT be touched
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_does_not_clear_tasks_belonging_to_other_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            // Both users coincidentally have tasks in the same category ID space.
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            // Other user's task has the same category ID — must NOT be touched.
            var otherTask = await SeedTaskAsync(context, otherUserId, category.Id, _now.AddHours(-1));
            var otherVersionBefore = otherTask.Version;

            var handler = CreateHandler(context, userId);
            await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            var dbOtherTask = await context.Tasks.FirstAsync(t => t.Id == otherTask.Id);
            dbOtherTask.CategoryId.Should().Be(category.Id, "other user's task must not be cleared");
            dbOtherTask.Version.Should().Be(otherVersionBefore, "other user's task version must not change");
        }

        [Fact]
        public async Task Handle_does_not_clear_tasks_assigned_to_different_category()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var categoryToDelete = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));
            var otherCategory = await SeedCategoryAsync(context, userId, "Personal", _now.AddHours(-1));

            // Task assigned to the OTHER category — must not be touched.
            var unrelatedTask = await SeedTaskAsync(context, userId, otherCategory.Id, _now.AddHours(-1));
            var versionBefore = unrelatedTask.Version;

            var handler = CreateHandler(context, userId);
            await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = categoryToDelete.Id, RowVersion = categoryToDelete.RowVersion },
                CancellationToken.None);

            var dbTask = await context.Tasks.FirstAsync(t => t.Id == unrelatedTask.Id);
            dbTask.CategoryId.Should().Be(otherCategory.Id, "task in a different category must not be cleared");
            dbTask.Version.Should().Be(versionBefore);
        }

        [Fact]
        public async Task Handle_does_not_clear_already_soft_deleted_tasks()
        {
            // ClearCategoryFromTasksAsync filters !IsDeleted, so soft-deleted tasks
            // are intentionally skipped — they will be deleted from the client anyway.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            // Seed a task with the category, then soft-delete it.
            var deletedTask = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Deleted task",
                null, null, null, null, null,
                category.Id, TaskPriority.Normal, _now.AddHours(-1)).Value!;

            typeof(TaskItem).GetProperty(nameof(TaskItem.IsDeleted))!
                .SetValue(deletedTask, true);

            await context.Tasks.AddAsync(deletedTask);
            await context.SaveChangesAsync();

            var versionBefore = deletedTask.Version;
            context.ChangeTracker.Clear();

            var handler = CreateHandler(context, userId);
            await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            // Reload with IgnoreQueryFilters since the task is soft-deleted.
            var dbTask = await context.Tasks
                .IgnoreQueryFilters()
                .FirstAsync(t => t.Id == deletedTask.Id);

            dbTask.CategoryId.Should().Be(category.Id,
                "soft-deleted tasks are excluded from ClearCategoryFromTasksAsync");
            dbTask.Version.Should().Be(versionBefore,
                "soft-deleted task version must not be incremented");
        }

        // -----------------------------------------------------------------------
        // Idempotency: category already gone
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_when_category_already_deleted_still_clears_residual_task_refs_and_returns_ok()
        {
            // If the category was previously deleted but a task still holds the FK
            // (e.g. a prior run crashed after the soft-delete but before the clear),
            // the handler must clear those refs and return OK so the caller can retry safely.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            // Manually soft-delete the category directly in the DB to simulate a partial failure.
            var tracked = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            tracked.SoftDelete(_now.AddMinutes(-30));
            context.TaskCategories.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Seed a task that still points to the (now-deleted) category ID.
            // This represents a residual FK from a previously partial operation.
            // We insert directly (bypassing FK via NoAction behavior).
            var residualTask = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Residual task",
                null, null, null, null, null,
                null, // null so no FK violation at insert time
                TaskPriority.Normal,
                _now.AddHours(-1)).Value!;

            await context.Tasks.AddAsync(residualTask);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Now set CategoryId to the deleted category ID to simulate the residual state.
            await context.Tasks
                .Where(t => t.Id == residualTask.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, category.Id));

            // Act: delete handler sees null (category is gone), but still clears task refs.
            var handler = CreateHandler(context, userId);
            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id },
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue("idempotent delete must return OK even if category is gone");

            // Residual task FK must be cleared.
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == residualTask.Id);
            dbTask.CategoryId.Should().BeNull("residual task refs must be cleared even on the retry path");
        }

        // -----------------------------------------------------------------------
        // Ownership guard
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_when_category_belongs_to_different_user_returns_not_found_and_leaves_db_unchanged()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();

            // Category belongs to otherUser.
            var category = await SeedCategoryAsync(context, otherUserId, "Work", _now.AddHours(-1));

            // A task that the requesting user owns and has this category.
            var task = await SeedTaskAsync(context, userId, category.Id, _now.AddHours(-1));
            var versionBefore = task.Version;

            // Act as userId — category does not belong to this user.
            var handler = CreateHandler(context, userId);
            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id },
                CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Categories.NotFound");

            // Category must remain live.
            var dbCategory = await context.TaskCategories.FirstAsync(c => c.Id == category.Id);
            dbCategory.IsDeleted.Should().BeFalse();

            // Task must be untouched.
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == task.Id);
            dbTask.CategoryId.Should().Be(category.Id);
            dbTask.Version.Should().Be(versionBefore);
        }

        // -----------------------------------------------------------------------
        // Outbox
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_emits_exactly_one_outbox_message_on_successful_delete()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var userId = Guid.NewGuid();

            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            var handler = CreateHandler(context, userId);
            var result = await handler.Handle(
                new DeleteTaskCategoryCommand { CategoryId = category.Id, RowVersion = category.RowVersion },
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().HaveCount(1, "exactly one outbox message must be emitted per delete");
            outboxMessages[0].AggregateType.Should().Be(nameof(TaskCategory));
        }
    }
}
