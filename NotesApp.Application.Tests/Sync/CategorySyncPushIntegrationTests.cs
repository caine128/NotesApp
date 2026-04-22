using NotesApp.Application.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Commands.SyncPush;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Integration tests for SyncPushCommandHandler (sync push) with a focus on the
    /// category entity, using a real SQL Server database.
    ///
    /// Covers:
    /// - Category create via sync push persists to DB with a server-assigned ID.
    /// - Category update via sync push renames the category and increments Version in DB.
    /// - Category update version mismatch leaves the DB unchanged.
    /// - Category delete via sync push soft-deletes the category in DB.
    ///
    /// KEY behavioral contract test:
    /// - Category delete via sync push does NOT call ClearCategoryFromTasksAsync —
    ///   the task's CategoryId and Version are untouched.
    ///   (Contrast: REST DELETE /categories/{id} DOES clear tasks server-side.)
    ///
    /// Within-push resolution:
    /// - A task created in the same push as a new category uses the clientId as CategoryId;
    ///   the handler resolves it to the server-assigned ID before persisting.
    /// </summary>
    public sealed class CategorySyncPushIntegrationTests
    {
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Creates a User + UserDevice in the DB.  UserDevice has a FK to Users so a real
        /// User record must exist before the device can be inserted.
        /// Returns (userId, deviceId) for use in the ICurrentUserService mock and push command.
        /// </summary>
        private static async Task<(Guid userId, Guid deviceId)> SeedUserAndDeviceAsync(
            AppDbContext context,
            DateTime utcNow)
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
            AppDbContext context,
            Guid userId,
            DateTime utcNow)
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
                new AttachmentRepository(context), // REFACTORED: added for task-attachments feature
                // REFACTORED: added recurring-task repos for recurring-tasks feature
                new RecurringTaskRootRepository(context),
                new RecurringTaskSeriesRepository(context),
                new RecurringTaskSubtaskRepository(context),
                new RecurringTaskExceptionRepository(context),
                new OutboxRepository(context),
                new UnitOfWork(context),
                clock.Object,
                new Mock<ILogger<SyncPushCommandHandler>>().Object);
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
                userId, new DateOnly(2025, 6, 1), "Test task",
                null, null, null, null, null,
                categoryId,NotesApp.Domain.Common.TaskPriority.Normal ,utcNow).Value!;
            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return task;
        }

        // -----------------------------------------------------------------------
        // Category Create
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_CategoryCreate_persists_category_to_db_with_server_id()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var categoryClientId = Guid.NewGuid();
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Created = new[]
                    {
                        new CategoryCreatedPushItemDto
                        {
                            ClientId = categoryClientId,
                            Name = "Work"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var categoryResult = result.Value.Categories.Created.Should().ContainSingle().Subject;
            categoryResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            categoryResult.ClientId.Should().Be(categoryClientId);
            categoryResult.ServerId.Should().NotBe(categoryClientId,
                "server assigns its own stable ID, distinct from the client-generated ID");
            categoryResult.Version.Should().Be(1);

            // Verify the category was actually persisted to the DB.
            var dbCategory = await context.TaskCategories
                .FirstOrDefaultAsync(c => c.Id == categoryResult.ServerId);
            dbCategory.Should().NotBeNull();
            dbCategory!.Name.Should().Be("Work");
            dbCategory.UserId.Should().Be(userId);
            dbCategory.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Handle_CategoryCreate_emits_outbox_message()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Created = new[]
                    {
                        new CategoryCreatedPushItemDto { ClientId = Guid.NewGuid(), Name = "Personal" }
                    }
                }
            };

            await handler.Handle(command, CancellationToken.None);

            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().ContainSingle(m => m.AggregateType == nameof(TaskCategory));
        }

        // -----------------------------------------------------------------------
        // Category Update
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_CategoryUpdate_renames_category_in_db_and_increments_version()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Updated = new[]
                    {
                        new CategoryUpdatedPushItemDto
                        {
                            Id = category.Id,
                            ExpectedVersion = category.Version, // Version = 1
                            Name = "Lifestyle"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var updateResult = result.Value.Categories.Updated.Should().ContainSingle().Subject;
            updateResult.Status.Should().Be(SyncPushUpdatedStatus.Updated);
            updateResult.NewVersion.Should().Be(2);

            // Verify the rename and version bump are in the DB.
            var dbCategory = await context.TaskCategories.FirstAsync(c => c.Id == category.Id);
            dbCategory.Name.Should().Be("Lifestyle");
            dbCategory.Version.Should().Be(2);
        }

        [Fact]
        public async Task Handle_CategoryUpdate_version_mismatch_does_not_modify_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            // Simulate a newer server version by bumping directly in the DB.
            await context.TaskCategories
                .Where(c => c.Id == category.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Version, 3L));
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Updated = new[]
                    {
                        new CategoryUpdatedPushItemDto
                        {
                            Id = category.Id,
                            ExpectedVersion = 1, // Stale; server is at version 3.
                            Name = "Should not persist"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var updateResult = result.Value.Categories.Updated.Should().ContainSingle().Subject;
            updateResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            updateResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);
            updateResult.Conflict.ServerCategory.Should().NotBeNull();
            updateResult.Conflict.ServerVersion.Should().Be(3);

            // The category must remain unchanged in DB.
            var dbCategory = await context.TaskCategories.FirstAsync(c => c.Id == category.Id);
            dbCategory.Name.Should().Be("Work", "version mismatch must not persist the client's rename");
            dbCategory.Version.Should().Be(3);
        }

        // -----------------------------------------------------------------------
        // Category Delete
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_CategoryDelete_soft_deletes_category_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Deleted = new[] { new CategoryDeletedPushItemDto { Id = category.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Categories.Deleted.Should().ContainSingle(r =>
                r.Id == category.Id && r.Status == SyncPushDeletedStatus.Deleted);

            // Category must be soft-deleted in the DB.
            var dbCategory = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            dbCategory.IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_CategoryDelete_does_NOT_clear_task_category_fk_in_db()
        {
            // ═══════════════════════════════════════════════════════════════════
            // KEY BEHAVIORAL CONTRACT TEST
            //
            // Sync push delete path: category is soft-deleted but tasks are NOT
            // touched server-side.  The mobile client is responsible for sending
            // all affected task updates (CategoryId = null) in the same push.
            //
            // Compare to REST DELETE /categories/{id} which calls
            // ClearCategoryFromTasksAsync and nulls the FK server-side.
            // ═══════════════════════════════════════════════════════════════════
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));
            var task = await SeedTaskAsync(context, userId, category.Id, _now.AddHours(-1));
            var taskVersionBefore = task.Version;

            var handler = CreatePushHandler(context, userId, _now);

            // Send only the category delete — no task update included.
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Deleted = new[] { new CategoryDeletedPushItemDto { Id = category.Id } }
                }
                // NOTE: no Tasks.Updated with CategoryId = null
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            // Category soft-deleted — confirmed.
            var dbCategory = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            dbCategory.IsDeleted.Should().BeTrue();

            // Task must be UNTOUCHED — CategoryId still set, Version unchanged.
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == task.Id);
            dbTask.CategoryId.Should().Be(category.Id,
                "sync push delete must NOT clear task FK server-side; " +
                "the mobile client is responsible for sending task updates");
            dbTask.Version.Should().Be(taskVersionBefore,
                "task version must not be incremented by a sync push category delete");
        }

        [Fact]
        public async Task Handle_CategoryDelete_when_category_already_gone_returns_not_found()
        {
            // In real DB with the global query filter applied, a previously soft-deleted
            // category is returned as null by GetByIdUntrackedAsync, so the handler returns
            // NotFound (not AlreadyDeleted which is the unit-test mock path).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var category = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            // Soft-delete directly.
            var tracked = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);
            tracked.SoftDelete(_now.AddMinutes(-5));
            context.TaskCategories.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Deleted = new[] { new CategoryDeletedPushItemDto { Id = category.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            // Global query filter means GetByIdUntrackedAsync returns null → NotFound status.
            result.Value.Categories.Deleted.Should().ContainSingle(r =>
                r.Id == category.Id && r.Status == SyncPushDeletedStatus.NotFound);
        }

        // -----------------------------------------------------------------------
        // Within-push category client ID resolution
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_within_push_task_create_with_category_client_id_resolves_to_server_id_in_db()
        {
            // When a category and a task referencing it are created in the same push,
            // the task's CategoryId field contains the category's client-side GUID.
            // The handler must resolve this to the server-assigned ID before persisting
            // so the task row in DB holds a valid FK to the new category.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var categoryClientId = Guid.NewGuid();
            var taskClientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Categories = new SyncPushCategoriesDto
                {
                    Created = new[]
                    {
                        new CategoryCreatedPushItemDto
                        {
                            ClientId = categoryClientId,
                            Name = "Work"
                        }
                    }
                },
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                        new TaskCreatedPushItemDto
                        {
                            ClientId = taskClientId,
                            Date = new DateOnly(2025, 6, 1),
                            Title = "Task in new category",
                            CategoryId = categoryClientId // within-push client ID reference
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var catResult = result.Value.Categories.Created.Should().ContainSingle().Subject;
            catResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            var serverCategoryId = catResult.ServerId;

            var taskResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            taskResult.Status.Should().Be(SyncPushCreatedStatus.Created,
                "task creation must succeed because category client ID is resolved within the push");
            taskResult.Conflict.Should().BeNull();
            var serverTaskId = taskResult.ServerId;

            // Verify the task in the DB references the server category ID (not the client ID).
            var dbTask = await context.Tasks.FirstAsync(t => t.Id == serverTaskId);
            dbTask.CategoryId.Should().Be(serverCategoryId,
                "the within-push client ID must be resolved to the server ID before persisting");
            dbTask.CategoryId.Should().NotBe(categoryClientId,
                "the client-generated ID must never appear in the DB");
        }

        [Fact]
        public async Task Handle_task_create_with_existing_server_category_id_resolves_correctly()
        {
            // Unlike the within-push path, a task referencing an ALREADY-PERSISTED category
            // by its server ID (not a fresh client ID) must also resolve correctly.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var existingCategory = await SeedCategoryAsync(context, userId, "Work", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var taskClientId = Guid.NewGuid();

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Created = new[]
                    {
                        new TaskCreatedPushItemDto
                        {
                            ClientId = taskClientId,
                            Date = new DateOnly(2025, 6, 1),
                            Title = "Task with existing category",
                            CategoryId = existingCategory.Id // server-side ID, already in DB
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            taskResult.Status.Should().Be(SyncPushCreatedStatus.Created);

            var dbTask = await context.Tasks.FirstAsync(t => t.Id == taskResult.ServerId);
            dbTask.CategoryId.Should().Be(existingCategory.Id);
        }
    }
}
