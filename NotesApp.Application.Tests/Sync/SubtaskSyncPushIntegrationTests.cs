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
    /// Integration tests for SyncPushCommandHandler (sync push) with a focus on the
    /// Subtask entity, using a real SQL Server database.
    ///
    /// Covers:
    /// - Subtask create via sync push persists to DB with a server-assigned ID.
    /// - Within-push task resolution: subtask created in the same push as its parent task
    ///   uses TaskClientId; the handler resolves it to the server-assigned TaskId.
    /// - Creating a subtask with an unknown parent fails with ParentNotFound.
    /// - Subtask update (text, completion, position) applies changes and increments version.
    /// - Subtask update with all-null fields is a no-op (returns Updated, DB unchanged).
    /// - Subtask update version mismatch returns Failed with ServerSubtask in conflict detail.
    /// - Subtask delete via sync push soft-deletes the subtask in DB.
    ///
    /// KEY behavioral contract test:
    /// - Task delete via sync push triggers a server-side safety sweep
    ///   (SoftDeleteAllForTaskAsync) that soft-deletes any remaining subtasks even if
    ///   the client did not send explicit SubtaskDeleted items.
    /// </summary>
    public sealed class SubtaskSyncPushIntegrationTests
    {
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Creates a User + UserDevice in the DB and returns (userId, deviceId).
        /// UserDevice has a FK to Users so a real User record must exist first.
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
                new TaskRepository(context),
                new NoteRepository(context),
                new BlockRepository(context),
                new UserDeviceRepository(context),
                new CategoryRepository(context),
                new SubtaskRepository(context),
                new OutboxRepository(context),
                new UnitOfWork(context),
                clock.Object,
                new Mock<ILogger<SyncPushCommandHandler>>().Object);
        }

        private static async Task<TaskItem> SeedTaskAsync(
            AppDbContext context,
            Guid userId,
            DateTime utcNow)
        {
            var task = TaskItem.Create(
                userId, new DateOnly(2025, 6, 1), "Test task",
                null, null, null, null, null,
                null, TaskPriority.Normal, utcNow).Value!;
            await context.Tasks.AddAsync(task);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return task;
        }

        private static async Task<Subtask> SeedSubtaskAsync(
            AppDbContext context,
            Guid userId,
            Guid taskId,
            string text,
            string position,
            DateTime utcNow)
        {
            var subtask = Subtask.Create(userId, taskId, text, position, utcNow).Value!;
            await context.Subtasks.AddAsync(subtask);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return subtask;
        }

        // -----------------------------------------------------------------------
        // Subtask Create
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_SubtaskCreate_persists_subtask_to_db_with_server_id()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var handler = CreatePushHandler(context, userId, _now);

            var subtaskClientId = Guid.NewGuid();
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = new[]
                    {
                        new SubtaskCreatedPushItemDto
                        {
                            ClientId = subtaskClientId,
                            TaskId = task.Id,
                            Text = "Buy groceries",
                            Position = "a0"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var createResult = result.Value.Subtasks.Created.Should().ContainSingle().Subject;
            createResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            createResult.ClientId.Should().Be(subtaskClientId);
            createResult.ServerId.Should().NotBe(subtaskClientId,
                "server assigns its own stable ID, distinct from the client-generated ID");
            createResult.Version.Should().Be(1);

            // Verify the subtask was actually persisted to the DB.
            var dbSubtask = await context.Subtasks
                .FirstOrDefaultAsync(s => s.Id == createResult.ServerId);
            dbSubtask.Should().NotBeNull();
            dbSubtask!.Text.Should().Be("Buy groceries");
            dbSubtask.Position.Should().Be("a0");
            dbSubtask.TaskId.Should().Be(task.Id);
            dbSubtask.UserId.Should().Be(userId);
            dbSubtask.IsCompleted.Should().BeFalse();
            dbSubtask.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Handle_SubtaskCreate_emits_outbox_message()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var handler = CreatePushHandler(context, userId, _now);

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = new[]
                    {
                        new SubtaskCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            TaskId = task.Id,
                            Text = "Walk the dog",
                            Position = "a0"
                        }
                    }
                }
            };

            await handler.Handle(command, CancellationToken.None);

            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().ContainSingle(m => m.AggregateType == nameof(Subtask));
        }

        [Fact]
        public async Task Handle_SubtaskCreate_within_push_task_resolution_uses_taskClientId()
        {
            // When a task and a subtask are created in the same push,
            // the subtask uses TaskClientId (not TaskId) to reference the parent.
            // The handler must resolve TaskClientId to the server-assigned TaskId before persisting.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var taskClientId = Guid.NewGuid();
            var subtaskClientId = Guid.NewGuid();

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
                            Title = "Parent task"
                        }
                    }
                },
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = new[]
                    {
                        new SubtaskCreatedPushItemDto
                        {
                            ClientId = subtaskClientId,
                            TaskClientId = taskClientId, // within-push reference
                            Text = "Subtask of new task",
                            Position = "a0"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var taskResult = result.Value.Tasks.Created.Should().ContainSingle().Subject;
            taskResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            var serverTaskId = taskResult.ServerId;

            var subtaskResult = result.Value.Subtasks.Created.Should().ContainSingle().Subject;
            subtaskResult.Status.Should().Be(SyncPushCreatedStatus.Created,
                "subtask creation must succeed because TaskClientId is resolved within the push");
            subtaskResult.Conflict.Should().BeNull();

            // The subtask in DB must reference the server-assigned TaskId, not the client ID.
            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtaskResult.ServerId);
            dbSubtask.TaskId.Should().Be(serverTaskId,
                "the within-push TaskClientId must be resolved to the server-assigned TaskId");
            dbSubtask.TaskId.Should().NotBe(taskClientId,
                "the client-generated ID must never appear in the DB as a FK");
        }

        [Fact]
        public async Task Handle_SubtaskCreate_with_unknown_parent_task_returns_failed()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var handler = CreatePushHandler(context, userId, _now);

            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Created = new[]
                    {
                        new SubtaskCreatedPushItemDto
                        {
                            ClientId = Guid.NewGuid(),
                            TaskId = Guid.NewGuid(), // non-existent task
                            Text = "Orphan subtask",
                            Position = "a0"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var createResult = result.Value.Subtasks.Created.Should().ContainSingle().Subject;
            createResult.Status.Should().Be(SyncPushCreatedStatus.Failed);
            createResult.Conflict!.ConflictType.Should().Be(SyncConflictType.ParentNotFound);

            // Nothing should have been persisted.
            var anySubtask = await context.Subtasks.AnyAsync(s => s.UserId == userId);
            anySubtask.Should().BeFalse();
        }

        // -----------------------------------------------------------------------
        // Subtask Update
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_SubtaskUpdate_changes_text_in_db_and_increments_version()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Old text", "a0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = new[]
                    {
                        new SubtaskUpdatedPushItemDto
                        {
                            Id = subtask.Id,
                            ExpectedVersion = subtask.Version,
                            Text = "New text"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var updateResult = result.Value.Subtasks.Updated.Should().ContainSingle().Subject;
            updateResult.Status.Should().Be(SyncPushUpdatedStatus.Updated);
            updateResult.NewVersion.Should().Be(2);

            // Verify DB was updated.
            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.Text.Should().Be("New text");
            dbSubtask.Version.Should().Be(2);
        }

        [Fact]
        public async Task Handle_SubtaskUpdate_marks_subtask_completed_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Buy groceries", "a0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = new[]
                    {
                        new SubtaskUpdatedPushItemDto
                        {
                            Id = subtask.Id,
                            ExpectedVersion = subtask.Version,
                            IsCompleted = true
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Updated.Should().ContainSingle(r =>
                r.Status == SyncPushUpdatedStatus.Updated && r.NewVersion == 2);

            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.IsCompleted.Should().BeTrue();
            dbSubtask.Version.Should().Be(2);
        }

        [Fact]
        public async Task Handle_SubtaskUpdate_changes_position_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Buy groceries", "a0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = new[]
                    {
                        new SubtaskUpdatedPushItemDto
                        {
                            Id = subtask.Id,
                            ExpectedVersion = subtask.Version,
                            Position = "b0"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Updated.Should().ContainSingle(r =>
                r.Status == SyncPushUpdatedStatus.Updated && r.NewVersion == 2);

            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.Position.Should().Be("b0");
            dbSubtask.Version.Should().Be(2);
        }

        [Fact]
        public async Task Handle_SubtaskUpdate_with_all_null_fields_returns_updated_without_modifying_db()
        {
            // When all optional fields (Text, IsCompleted, Position) are null,
            // hasChanges = false — no outbox or DB write occurs.
            // The handler still returns Updated with the current version.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Original", "a0", _now.AddHours(-1));
            var versionBefore = subtask.Version;

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = new[]
                    {
                        new SubtaskUpdatedPushItemDto
                        {
                            Id = subtask.Id,
                            ExpectedVersion = subtask.Version
                            // Text = null, IsCompleted = null, Position = null
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Updated.Should().ContainSingle(r =>
                r.Status == SyncPushUpdatedStatus.Updated);

            // DB must be unchanged.
            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.Text.Should().Be("Original");
            dbSubtask.Version.Should().Be(versionBefore, "no fields were changed so version must not increment");

            // No outbox messages should have been emitted for this no-op update.
            var outboxMessages = await context.OutboxMessages.ToListAsync();
            outboxMessages.Should().BeEmpty("a no-op update must not produce an outbox message");
        }

        [Fact]
        public async Task Handle_SubtaskUpdate_version_mismatch_returns_failed_with_server_subtask()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Buy groceries", "a0", _now.AddHours(-1));

            // Simulate a newer server version by bumping directly in the DB.
            await context.Subtasks
                .Where(s => s.Id == subtask.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, 5L));
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Updated = new[]
                    {
                        new SubtaskUpdatedPushItemDto
                        {
                            Id = subtask.Id,
                            ExpectedVersion = 1, // stale; server is at version 5
                            Text = "Should not persist"
                        }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            var updateResult = result.Value.Subtasks.Updated.Should().ContainSingle().Subject;
            updateResult.Status.Should().Be(SyncPushUpdatedStatus.Failed);
            updateResult.Conflict!.ConflictType.Should().Be(SyncConflictType.VersionMismatch);
            updateResult.Conflict.ServerVersion.Should().Be(5);
            updateResult.Conflict.ServerSubtask.Should().NotBeNull(
                "version mismatch must return the current server state so the client can resolve");
            updateResult.Conflict.ServerSubtask!.Id.Should().Be(subtask.Id);
            updateResult.Conflict.ServerSubtask.Version.Should().Be(5);

            // DB must remain unchanged.
            var dbSubtask = await context.Subtasks.FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.Text.Should().Be("Buy groceries", "version mismatch must not persist the client's update");
            dbSubtask.Version.Should().Be(5);
        }

        // -----------------------------------------------------------------------
        // Subtask Delete
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_SubtaskDelete_soft_deletes_subtask_in_db()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Buy groceries", "a0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Deleted = new[] { new SubtaskDeletedPushItemDto { Id = subtask.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Deleted.Should().ContainSingle(r =>
                r.Id == subtask.Id && r.Status == SyncPushDeletedStatus.Deleted);

            // Subtask must be soft-deleted in the DB.
            var dbSubtask = await context.Subtasks
                .IgnoreQueryFilters()
                .FirstAsync(s => s.Id == subtask.Id);
            dbSubtask.IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_SubtaskDelete_when_subtask_already_gone_returns_not_found()
        {
            // With the global query filter applied, GetByIdUntrackedAsync returns null for
            // a soft-deleted subtask, so the handler returns NotFound (idempotent).
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Buy groceries", "a0", _now.AddHours(-1));

            // Soft-delete directly.
            var tracked = await context.Subtasks
                .IgnoreQueryFilters()
                .FirstAsync(s => s.Id == subtask.Id);
            tracked.SoftDelete(_now.AddMinutes(-5));
            context.Subtasks.Update(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var handler = CreatePushHandler(context, userId, _now);
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Subtasks = new SyncPushSubtasksDto
                {
                    Deleted = new[] { new SubtaskDeletedPushItemDto { Id = subtask.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            // Global query filter means GetByIdUntrackedAsync returns null → NotFound status.
            result.Value.Subtasks.Deleted.Should().ContainSingle(r =>
                r.Id == subtask.Id && r.Status == SyncPushDeletedStatus.NotFound);
        }

        // -----------------------------------------------------------------------
        // Task delete → safety sweep
        // -----------------------------------------------------------------------

        // -----------------------------------------------------------------------
        // Task delete + explicit subtask deletes
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_TaskDelete_with_explicit_subtask_deletes_soft_deletes_all()
        {
            // ═══════════════════════════════════════════════════════════════════
            // KEY BEHAVIORAL CONTRACT TEST
            //
            // The client is responsible for sending explicit SubtaskDeleted items
            // alongside the TaskDeleted item.  The server does NOT perform a
            // server-side sweep; it simply applies each explicit delete.
            // ═══════════════════════════════════════════════════════════════════
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask1 = await SeedSubtaskAsync(context, userId, task.Id, "Step 1", "a0", _now.AddHours(-1));
            var subtask2 = await SeedSubtaskAsync(context, userId, task.Id, "Step 2", "b0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);

            // Client sends the task delete AND both explicit subtask deletes.
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = task.Id } }
                },
                Subtasks = new SyncPushSubtasksDto
                {
                    Deleted = new[]
                    {
                        new SubtaskDeletedPushItemDto { Id = subtask1.Id },
                        new SubtaskDeletedPushItemDto { Id = subtask2.Id }
                    }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            result.Value.Tasks.Deleted.Should().ContainSingle(r =>
                r.Id == task.Id && r.Status == SyncPushDeletedStatus.Deleted);

            result.Value.Subtasks.Deleted.Should().HaveCount(2);
            result.Value.Subtasks.Deleted.Should().AllSatisfy(r =>
                r.Status.Should().Be(SyncPushDeletedStatus.Deleted));

            // Both subtasks must be soft-deleted in DB.
            var allSubtasks = await context.Subtasks
                .IgnoreQueryFilters()
                .Where(s => s.TaskId == task.Id)
                .ToListAsync();

            allSubtasks.Should().HaveCount(2);
            allSubtasks.Should().AllSatisfy(s =>
                s.IsDeleted.Should().BeTrue("each subtask was explicitly deleted by the client"));
        }

        [Fact]
        public async Task Handle_TaskDelete_without_subtask_deletes_leaves_subtasks_untouched()
        {
            // The server does NOT perform a safety sweep.
            // If the client omits subtask deletes (e.g. bug on client), the subtasks
            // remain non-deleted on the server until explicit deletes arrive.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var (userId, deviceId) = await SeedUserAndDeviceAsync(context, _now);
            var task = await SeedTaskAsync(context, userId, _now.AddHours(-1));
            var subtask = await SeedSubtaskAsync(context, userId, task.Id, "Orphaned step", "a0", _now.AddHours(-1));

            var handler = CreatePushHandler(context, userId, _now);

            // Delete the task only — no subtask deletes.
            var command = new SyncPushCommand
            {
                DeviceId = deviceId,
                ClientSyncTimestampUtc = _now,
                Tasks = new SyncPushTasksDto
                {
                    Deleted = new[] { new TaskDeletedPushItemDto { Id = task.Id } }
                }
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Tasks.Deleted.Should().ContainSingle(r =>
                r.Id == task.Id && r.Status == SyncPushDeletedStatus.Deleted);

            // Subtask must NOT have been swept — it remains non-deleted.
            var dbSubtask = await context.Subtasks
                .IgnoreQueryFilters()
                .FirstAsync(s => s.Id == subtask.Id);

            dbSubtask.IsDeleted.Should().BeFalse(
                "the server does not sweep subtasks; the client must send explicit SubtaskDeleted items");
        }
    }
}
