using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Models;
using NotesApp.Application.Sync.Queries;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Unit tests for GetSyncChangesQueryHandler.
    /// </summary>
    public sealed class GetSyncChangesQueryHandlerTests
    {
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<INoteRepository> _noteRepositoryMock = new();
        private readonly Mock<IBlockRepository> _blockRepositoryMock = new();
        private readonly Mock<IAssetRepository> _assetRepositoryMock = new();
        private readonly Mock<IUserDeviceRepository> _deviceRepositoryMock = new();
        private readonly Mock<ICategoryRepository> _categoryRepositoryMock = new();
        private readonly Mock<ISubtaskRepository> _subtaskRepositoryMock = new();
        private readonly Mock<IAttachmentRepository> _attachmentRepositoryMock = new();
        // REFACTORED: added recurring-task repository mocks for recurring-tasks feature
        private readonly Mock<IRecurringTaskRootRepository> _recurringRootRepositoryMock = new();
        private readonly Mock<IRecurringTaskSeriesRepository> _recurringSeriesRepositoryMock = new();
        private readonly Mock<IRecurringTaskSubtaskRepository> _recurringSeriesSubtaskRepositoryMock = new();
        private readonly Mock<IRecurringTaskExceptionRepository> _recurringExceptionRepositoryMock = new();
        // REFACTORED: added recurring attachment repository mock for recurring-task-attachments feature
        private readonly Mock<IRecurringTaskAttachmentRepository> _recurringAttachmentRepositoryMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<GetSyncChangesQueryHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();

        private GetSyncChangesQueryHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            _clockMock
                .Setup(c => c.UtcNow)
                .Returns(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

            // Setup empty returns for block, asset and category repositories
            _blockRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Block>>(new List<Block>()));

            _assetRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Asset>>(new List<Asset>()));

            _categoryRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskCategory>());

            _subtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subtask>());

            _attachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Attachment>());

            // REFACTORED: set up recurring-task repo mocks to return empty collections
            _recurringRootRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskRoot>());

            _recurringSeriesRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSeries>());

            _recurringSeriesSubtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSubtask>());

            _recurringExceptionRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskException>());

            // REFACTORED: set up recurring attachment repo mock to return empty collection
            _recurringAttachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskAttachment>());

            return new GetSyncChangesQueryHandler(
                _taskRepositoryMock.Object,
                _noteRepositoryMock.Object,
                _blockRepositoryMock.Object,
                _assetRepositoryMock.Object,
                _deviceRepositoryMock.Object,
                _categoryRepositoryMock.Object,
                _subtaskRepositoryMock.Object,
                _attachmentRepositoryMock.Object,
                _recurringRootRepositoryMock.Object,
                _recurringSeriesRepositoryMock.Object,
                _recurringSeriesSubtaskRepositoryMock.Object,
                _recurringExceptionRepositoryMock.Object,
                _recurringAttachmentRepositoryMock.Object, // REFACTORED: added for recurring-task-attachments feature
                _currentUserServiceMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_with_non_owned_device_returns_DeviceNotFound_error()
        {
            // Arrange
            var handler = CreateHandler();
            var otherUserId = Guid.NewGuid();
            var deviceId = Guid.NewGuid();

            // Device belongs to someone else
            var foreignDevice = UserDevice.Create(
                otherUserId,
                "token-123",
                DevicePlatform.Android,
                "Other device",
                DateTime.UtcNow).Value!;

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignDevice);

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: deviceId, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        [Fact]
        public async Task Handle_initial_sync_treats_all_non_deleted_items_as_created()
        {
            // Arrange
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var handler = CreateHandler(); // First: sets default empty returns

            var task = CreateTask(_userId, now, isDeleted: false);
            var note = CreateNote(_userId, now, isDeleted: false);

            // Override defaults with test-specific data
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskItem> { task });

            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Note> { note });

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            Result<SyncChangesDto> result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var dto = result.Value;

            dto.Tasks.Created.Should().HaveCount(1);
            dto.Tasks.Updated.Should().BeEmpty();
            dto.Tasks.Deleted.Should().BeEmpty();

            dto.Notes.Created.Should().HaveCount(1);
            dto.Notes.Updated.Should().BeEmpty();
            dto.Notes.Deleted.Should().BeEmpty();

            dto.Tasks.Created[0].Id.Should().Be(task.Id);
            dto.Notes.Created[0].Id.Should().Be(note.Id);

            dto.ServerTimestampUtc.Should().NotBe(default);
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_created_updated_and_deleted_correctly()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var handler = CreateHandler(); // First: sets default empty returns

            var createdTask = CreateTask(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedTask = CreateTask(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedTask = CreateTask(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            var createdNote = CreateNote(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedNote = CreateNote(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedNote = CreateNote(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            // Override defaults with test-specific data
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskItem> { createdTask, updatedTask, deletedTask });

            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Note> { createdNote, updatedNote, deletedNote });

            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Tasks.Created.Should().ContainSingle(t => t.Id == createdTask.Id);
            dto.Tasks.Updated.Should().ContainSingle(t => t.Id == updatedTask.Id);
            dto.Tasks.Deleted.Should().ContainSingle(t => t.Id == deletedTask.Id);

            dto.Notes.Created.Should().ContainSingle(n => n.Id == createdNote.Id);
            dto.Notes.Updated.Should().ContainSingle(n => n.Id == updatedNote.Id);
            dto.Notes.Deleted.Should().ContainSingle(n => n.Id == deletedNote.Id);

            dto.Tasks.Deleted[0].DeletedAtUtc.Should().Be(deletedTask.UpdatedAtUtc);
            dto.Notes.Deleted[0].DeletedAtUtc.Should().Be(deletedNote.UpdatedAtUtc);
        }

        [Fact]
        public async Task Handle_initial_sync_includes_blocks_as_created()
        {
            // Arrange
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Setup tasks/notes BEFORE CreateHandler (CreateHandler won't override these)
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets up block/asset defaults; task/note already set

            // Override block repo AFTER CreateHandler (last setup wins)
            var block = CreateBlock(_userId, now, isDeleted: false);
            _blockRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Block> { block });

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Blocks.Created.Should().ContainSingle(b => b.Id == block.Id);
            dto.Blocks.Updated.Should().BeEmpty();
            dto.Blocks.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_initial_sync_includes_assets_without_download_url()
        {
            // Arrange
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Setup tasks/notes BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets up block/asset defaults

            // Override asset repo AFTER CreateHandler
            var asset = CreateAsset(_userId, now, isDeleted: false);
            _assetRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Asset> { asset });

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Assets.Created.Should().ContainSingle(a => a.Id == asset.Id);
            dto.Assets.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_blocks_into_created_updated_deleted()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Setup tasks/notes BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets up block/asset defaults

            // Override block repo AFTER CreateHandler
            var createdBlock = CreateBlock(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedBlock = CreateBlock(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedBlock = CreateBlock(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            _blockRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Block> { createdBlock, updatedBlock, deletedBlock });

            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Blocks.Created.Should().ContainSingle(b => b.Id == createdBlock.Id);
            dto.Blocks.Updated.Should().ContainSingle(b => b.Id == updatedBlock.Id);
            dto.Blocks.Deleted.Should().ContainSingle(b => b.Id == deletedBlock.Id);
            dto.Blocks.Deleted[0].DeletedAtUtc.Should().Be(deletedBlock.UpdatedAtUtc);
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_assets_into_created_and_deleted_only()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Setup tasks/notes BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets up block/asset defaults

            // Override asset repo AFTER CreateHandler
            var createdAsset = CreateAsset(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var deletedAsset = CreateAsset(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            _assetRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Asset> { createdAsset, deletedAsset });

            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            // Assets have no "updated" bucket — they're immutable
            dto.Assets.Created.Should().ContainSingle(a => a.Id == createdAsset.Id);
            dto.Assets.Deleted.Should().ContainSingle(a => a.Id == deletedAsset.Id);
            dto.Assets.Deleted[0].DeletedAtUtc.Should().Be(deletedAsset.UpdatedAtUtc);
        }

        [Fact]
        public async Task Handle_with_deleted_device_returns_DeviceNotFound_error()
        {
            // Arrange
            var handler = CreateHandler();
            var deviceId = Guid.NewGuid();

            var device = UserDevice.Create(
                _userId,
                "token-456",
                DevicePlatform.IOS,
                "My device",
                DateTime.UtcNow).Value!;

            // Mark device as deleted via soft-delete
            typeof(UserDevice).GetProperty(nameof(UserDevice.IsDeleted))!
                .SetValue(device, true);

            _deviceRepositoryMock
                .Setup(r => r.GetByIdAsync(deviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(device);

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: deviceId, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Device.NotFound");
        }

        [Fact]
        public async Task Handle_with_null_device_id_skips_device_check_and_succeeds()
        {
            // Arrange
            // Setup tasks/notes BEFORE CreateHandler so they return empty
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, (DateTime?)null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, (DateTime?)null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();
            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _deviceRepositoryMock.Verify(
                r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_truncates_tasks_and_sets_HasMore_true()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;

            // 3 tasks created after since → all go to "created" bucket → total = 3 > 2
            var tasks = Enumerable.Range(0, 3)
                .Select(i => CreateTask(_userId,
                    createdAt: since.AddMinutes(i + 1),
                    updatedAt: since.AddMinutes(i + 1),
                    isDeleted: false))
                .ToList();

            // Setup BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(tasks));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();
            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.HasMoreTasks.Should().BeTrue();
            (dto.Tasks.Created.Count + dto.Tasks.Updated.Count + dto.Tasks.Deleted.Count)
                .Should().BeLessThanOrEqualTo(maxItems);
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_does_not_set_HasMore_when_within_limit()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 10;

            var task = CreateTask(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            // Setup BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem> { task }));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();
            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreTasks.Should().BeFalse();
            result.Value.HasMoreNotes.Should().BeFalse();
            result.Value.HasMoreBlocks.Should().BeFalse();
        }

        [Fact]
        public async Task Handle_initial_sync_includes_categories_as_created()
        {
            // Arrange
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets default empty returns for block/asset/category

            // Override category repo AFTER CreateHandler
            var category = CreateCategory(_userId, now, isDeleted: false);
            _categoryRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskCategory> { category });

            var query = new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Categories.Created.Should().ContainSingle(c => c.Id == category.Id);
            dto.Categories.Updated.Should().BeEmpty();
            dto.Categories.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_categories_into_created_updated_deleted()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdCategory = CreateCategory(_userId,
                createdAt: since.AddMinutes(1),
                updatedAt: since.AddMinutes(1),
                isDeleted: false);

            var updatedCategory = CreateCategory(_userId,
                createdAt: since.AddMinutes(-10),
                updatedAt: since.AddMinutes(2),
                isDeleted: false);

            var deletedCategory = CreateCategory(_userId,
                createdAt: since.AddMinutes(-20),
                updatedAt: since.AddMinutes(3),
                isDeleted: true);

            _categoryRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TaskCategory> { createdCategory, updatedCategory, deletedCategory });

            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;

            dto.Categories.Created.Should().ContainSingle(c => c.Id == createdCategory.Id);
            dto.Categories.Updated.Should().ContainSingle(c => c.Id == updatedCategory.Id);
            dto.Categories.Deleted.Should().ContainSingle(c => c.Id == deletedCategory.Id);
            dto.Categories.Deleted[0].DeletedAtUtc.Should().Be(deletedCategory.UpdatedAtUtc);
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_truncates_blocks_independently()
        {
            // Arrange
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 1;

            // Setup tasks/notes BEFORE CreateHandler
            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler(); // Sets up block/asset defaults

            // 2 blocks → total 2 > 1 → HasMoreBlocks; override AFTER CreateHandler
            var blocks = Enumerable.Range(0, 2)
                .Select(i => CreateBlock(_userId,
                    createdAt: since.AddMinutes(i + 1),
                    updatedAt: since.AddMinutes(i + 1),
                    isDeleted: false))
                .ToList();

            _blockRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(blocks);

            var query = new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems);

            // Act
            var result = await handler.Handle(query, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreBlocks.Should().BeTrue();
            result.Value.HasMoreTasks.Should().BeFalse();
            result.Value.HasMoreNotes.Should().BeFalse();

            var totalBlocks = result.Value.Blocks.Created.Count
                + result.Value.Blocks.Updated.Count
                + result.Value.Blocks.Deleted.Count;
            totalBlocks.Should().BeLessThanOrEqualTo(maxItems);
        }

        private static TaskItem CreateTask(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateTask(userId, createdAt, createdAt, isDeleted);
        }

        private static TaskItem CreateTask(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            // Use factory to respect invariants, then tweak timestamps/state.
            var result = TaskItem.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Task",
                "Desc",
                null,
                null,
                null,
                null,
                null,
                TaskPriority.Normal,
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var task = result.Value;

            typeof(TaskItem).GetProperty(nameof(TaskItem.CreatedAtUtc))!
                .SetValue(task, createdAt);

            typeof(TaskItem).GetProperty(nameof(TaskItem.UpdatedAtUtc))!
                .SetValue(task, updatedAt);

            if (isDeleted)
            {
                typeof(TaskItem).GetProperty(nameof(TaskItem.IsDeleted))!
                    .SetValue(task, true);
            }

            return task;
        }

        private static Note CreateNote(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateNote(userId, createdAt, createdAt, isDeleted);
        }

        private static Note CreateNote(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            // CHANGED: content parameter removed from Note.Create
            var result = Note.Create(
                userId,
                new DateOnly(2025, 1, 2),
                "Title",
                null,
                null,
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var note = result.Value;

            typeof(Note).GetProperty(nameof(Note.CreatedAtUtc))!
                .SetValue(note, createdAt);

            typeof(Note).GetProperty(nameof(Note.UpdatedAtUtc))!
                .SetValue(note, updatedAt);

            if (isDeleted)
            {
                typeof(Note).GetProperty(nameof(Note.IsDeleted))!
                    .SetValue(note, true);
            }

            return note;
        }

        private static Block CreateBlock(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateBlock(userId, createdAt, createdAt, isDeleted);
        }

        private static Block CreateBlock(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            var result = Block.CreateTextBlock(
                userId,
                Guid.NewGuid(),
                BlockParentType.Note,
                BlockType.Paragraph,
                "a0",
                "Test content",
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var block = result.Value!;

            typeof(Block).GetProperty(nameof(Block.CreatedAtUtc))!
                .SetValue(block, createdAt);

            typeof(Block).GetProperty(nameof(Block.UpdatedAtUtc))!
                .SetValue(block, updatedAt);

            if (isDeleted)
            {
                typeof(Block).GetProperty(nameof(Block.IsDeleted))!
                    .SetValue(block, true);
            }

            return block;
        }

        private static TaskCategory CreateCategory(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateCategory(userId, createdAt, createdAt, isDeleted);
        }

        private static TaskCategory CreateCategory(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            var result = TaskCategory.Create(userId, "Work", createdAt);
            result.IsSuccess.Should().BeTrue();
            var category = result.Value!;

            typeof(TaskCategory).GetProperty(nameof(TaskCategory.CreatedAtUtc))!
                .SetValue(category, createdAt);

            typeof(TaskCategory).GetProperty(nameof(TaskCategory.UpdatedAtUtc))!
                .SetValue(category, updatedAt);

            if (isDeleted)
            {
                typeof(TaskCategory).GetProperty(nameof(TaskCategory.IsDeleted))!
                    .SetValue(category, true);
            }

            return category;
        }

        private static Asset CreateAsset(
            Guid userId,
            DateTime createdAt,
            bool isDeleted)
        {
            return CreateAsset(userId, createdAt, createdAt, isDeleted);
        }

        private static Asset CreateAsset(
            Guid userId,
            DateTime createdAt,
            DateTime updatedAt,
            bool isDeleted)
        {
            var result = Asset.Create(
                userId,
                Guid.NewGuid(),
                "file.jpg",
                "image/jpeg",
                1024,
                "path/to/blob",
                createdAt);

            result.IsSuccess.Should().BeTrue();
            var asset = result.Value!;

            typeof(Asset).GetProperty(nameof(Asset.CreatedAtUtc))!
                .SetValue(asset, createdAt);

            typeof(Asset).GetProperty(nameof(Asset.UpdatedAtUtc))!
                .SetValue(asset, updatedAt);

            if (isDeleted)
            {
                typeof(Asset).GetProperty(nameof(Asset.IsDeleted))!
                    .SetValue(asset, true);
            }

            return asset;
        }

        // -----------------------------------------------------------------------
        // Subtasks
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_subtasks_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var subtask = CreateSubtask(_userId, Guid.NewGuid(), now, now, isDeleted: false);
            _subtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subtask> { subtask });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Created.Should().ContainSingle(s => s.Id == subtask.Id);
            result.Value.Subtasks.Updated.Should().BeEmpty();
            result.Value.Subtasks.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_subtasks_into_created_updated_deleted()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var taskId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdSubtask = CreateSubtask(_userId, taskId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var updatedSubtask = CreateSubtask(_userId, taskId, since.AddMinutes(-10), since.AddMinutes(2), isDeleted: false);
            var deletedSubtask = CreateSubtask(_userId, taskId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _subtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Subtask> { createdSubtask, updatedSubtask, deletedSubtask });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Subtasks.Created.Should().ContainSingle(s => s.Id == createdSubtask.Id);
            result.Value.Subtasks.Updated.Should().ContainSingle(s => s.Id == updatedSubtask.Id);
            result.Value.Subtasks.Deleted.Should().ContainSingle(s => s.Id == deletedSubtask.Id);
            result.Value.Subtasks.Deleted[0].DeletedAtUtc.Should().Be(deletedSubtask.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // Attachments (immutable — Created + Deleted only)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_attachments_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var attachment = CreateAttachment(_userId, Guid.NewGuid(), now, now, isDeleted: false);
            _attachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Attachment> { attachment });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Attachments.Created.Should().ContainSingle(a => a.Id == attachment.Id);
            result.Value.Attachments.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_attachments_into_created_and_deleted_only()
        {
            // Attachments are immutable — no Updated bucket.
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var taskId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdAttachment = CreateAttachment(_userId, taskId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var deletedAttachment = CreateAttachment(_userId, taskId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _attachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Attachment> { createdAttachment, deletedAttachment });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Attachments.Created.Should().ContainSingle(a => a.Id == createdAttachment.Id);
            result.Value.Attachments.Deleted.Should().ContainSingle(a => a.Id == deletedAttachment.Id);
            result.Value.Attachments.Deleted[0].DeletedAtUtc.Should().Be(deletedAttachment.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // RecurringRoots (immutable — Created + Deleted only)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_recurring_roots_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var root = CreateRecurringRoot(_userId, now, now, isDeleted: false);
            _recurringRootRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskRoot> { root });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringRoots.Created.Should().ContainSingle(r => r.Id == root.Id);
            result.Value.RecurringRoots.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_recurring_roots_into_created_and_deleted_only()
        {
            // RecurringTaskRoot is immutable — no domain update method, no Updated bucket.
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdRoot = CreateRecurringRoot(_userId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var deletedRoot = CreateRecurringRoot(_userId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _recurringRootRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskRoot> { createdRoot, deletedRoot });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringRoots.Created.Should().ContainSingle(r => r.Id == createdRoot.Id);
            result.Value.RecurringRoots.Deleted.Should().ContainSingle(r => r.Id == deletedRoot.Id);
            result.Value.RecurringRoots.Deleted[0].DeletedAtUtc.Should().Be(deletedRoot.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // RecurringSeries
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_recurring_series_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var series = CreateRecurringSeries(_userId, now, now, isDeleted: false);
            _recurringSeriesRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSeries> { series });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringSeries.Created.Should().ContainSingle(s => s.Id == series.Id);
            result.Value.RecurringSeries.Updated.Should().BeEmpty();
            result.Value.RecurringSeries.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_recurring_series_into_created_updated_deleted()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdSeries = CreateRecurringSeries(_userId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var updatedSeries = CreateRecurringSeries(_userId, since.AddMinutes(-10), since.AddMinutes(2), isDeleted: false);
            var deletedSeries = CreateRecurringSeries(_userId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _recurringSeriesRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSeries> { createdSeries, updatedSeries, deletedSeries });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringSeries.Created.Should().ContainSingle(s => s.Id == createdSeries.Id);
            result.Value.RecurringSeries.Updated.Should().ContainSingle(s => s.Id == updatedSeries.Id);
            result.Value.RecurringSeries.Deleted.Should().ContainSingle(s => s.Id == deletedSeries.Id);
            result.Value.RecurringSeries.Deleted[0].DeletedAtUtc.Should().Be(deletedSeries.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // RecurringSeriesSubtasks
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_recurring_series_subtasks_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var seriesSubtask = CreateRecurringSeriesSubtask(_userId, Guid.NewGuid(), now, now, isDeleted: false);
            _recurringSeriesSubtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSubtask> { seriesSubtask });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringSeriesSubtasks.Created.Should().ContainSingle(s => s.Id == seriesSubtask.Id);
            result.Value.RecurringSeriesSubtasks.Updated.Should().BeEmpty();
            result.Value.RecurringSeriesSubtasks.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_recurring_series_subtasks_into_created_updated_deleted()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var seriesId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var created = CreateRecurringSeriesSubtask(_userId, seriesId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var updated = CreateRecurringSeriesSubtask(_userId, seriesId, since.AddMinutes(-10), since.AddMinutes(2), isDeleted: false);
            var deleted = CreateRecurringSeriesSubtask(_userId, seriesId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _recurringSeriesSubtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSubtask> { created, updated, deleted });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringSeriesSubtasks.Created.Should().ContainSingle(s => s.Id == created.Id);
            result.Value.RecurringSeriesSubtasks.Updated.Should().ContainSingle(s => s.Id == updated.Id);
            result.Value.RecurringSeriesSubtasks.Deleted.Should().ContainSingle(s => s.Id == deleted.Id);
            result.Value.RecurringSeriesSubtasks.Deleted[0].DeletedAtUtc.Should().Be(deleted.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // RecurringExceptions — inline subtask behaviour
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_inlines_exception_subtasks_into_recurring_exceptions()
        {
            // On initial sync, override exceptions include their subtasks inline.
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var overrideException = CreateRecurringOverrideException(_userId, Guid.NewGuid(), now, now, isDeleted: false);
            var exceptionSubtask = CreateRecurringExceptionSubtask(_userId, overrideException.Id, now, now, isDeleted: false);

            _recurringExceptionRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskException> { overrideException });

            // Batch-load called with the override exception's ID
            _recurringSeriesSubtaskRepositoryMock
                .Setup(r => r.GetByExceptionIdsAsync(
                    It.IsAny<IReadOnlyList<Guid>>(),
                    _userId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskSubtask> { exceptionSubtask });

            // Safety net: attachment batch-load returns empty (HasAttachmentOverride is false)
            _recurringAttachmentRepositoryMock
                .Setup(r => r.GetByExceptionIdsAsync(
                    It.IsAny<IReadOnlyList<Guid>>(),
                    _userId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskAttachment>());

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            var exceptionDto = result.Value.RecurringExceptions.Created
                .Should().ContainSingle(e => e.Id == overrideException.Id)
                .Which;
            exceptionDto.Subtasks.Should().ContainSingle(s => s.Id == exceptionSubtask.Id);
        }

        [Fact]
        public async Task Handle_incremental_sync_exception_inline_subtasks_are_empty()
        {
            // On incremental sync the inline lists are always empty;
            // exception subtask changes arrive via the top-level RecurringSeriesSubtasks bucket.
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var overrideException = CreateRecurringOverrideException(
                _userId, Guid.NewGuid(), since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);

            _recurringExceptionRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskException> { overrideException });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            // GetByExceptionIdsAsync must NOT be called on incremental sync
            _recurringSeriesSubtaskRepositoryMock.Verify(
                r => r.GetByExceptionIdsAsync(
                    It.IsAny<IReadOnlyList<Guid>>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            var exceptionDto = result.Value.RecurringExceptions.Created
                .Should().ContainSingle(e => e.Id == overrideException.Id)
                .Which;
            exceptionDto.Subtasks.Should().BeEmpty();
        }

        // -----------------------------------------------------------------------
        // RecurringAttachments (immutable — Created + Deleted only)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_initial_sync_includes_recurring_attachments_as_created()
        {
            var since = (DateTime?)null;
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var attachment = CreateRecurringAttachment(_userId, Guid.NewGuid(), now, now, isDeleted: false);
            _recurringAttachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskAttachment> { attachment });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: null, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringAttachments.Created.Should().ContainSingle(a => a.Id == attachment.Id);
            result.Value.RecurringAttachments.Deleted.Should().BeEmpty();
        }

        [Fact]
        public async Task Handle_incremental_sync_categorises_recurring_attachments_into_created_and_deleted_only()
        {
            // RecurringTaskAttachment is immutable — no Updated bucket.
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var seriesId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var createdAttachment = CreateRecurringAttachment(_userId, seriesId, since.AddMinutes(1), since.AddMinutes(1), isDeleted: false);
            var deletedAttachment = CreateRecurringAttachment(_userId, seriesId, since.AddMinutes(-20), since.AddMinutes(3), isDeleted: true);

            _recurringAttachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RecurringTaskAttachment> { createdAttachment, deletedAttachment });

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.RecurringAttachments.Created.Should().ContainSingle(a => a.Id == createdAttachment.Id);
            result.Value.RecurringAttachments.Deleted.Should().ContainSingle(a => a.Id == deletedAttachment.Id);
            result.Value.RecurringAttachments.Deleted[0].DeletedAtUtc.Should().Be(deletedAttachment.UpdatedAtUtc);
        }

        // -----------------------------------------------------------------------
        // Pagination — HasMore flags for entities added in this feature set
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_truncates_assets_and_sets_HasMoreAssets_true()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            // 3 assets → total 3 > max 2 → HasMoreAssets
            var assets = Enumerable.Range(0, 3)
                .Select(i => CreateAsset(_userId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _assetRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(assets);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreAssets.Should().BeTrue();
            (result.Value.Assets.Created.Count + result.Value.Assets.Deleted.Count)
                .Should().BeLessThanOrEqualTo(maxItems);
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_truncates_attachments_and_sets_HasMoreAttachments_true()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var taskId = Guid.NewGuid();
            var attachments = Enumerable.Range(0, 3)
                .Select(i => CreateAttachment(_userId, taskId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _attachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachments);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreAttachments.Should().BeTrue();
            (result.Value.Attachments.Created.Count + result.Value.Attachments.Deleted.Count)
                .Should().BeLessThanOrEqualTo(maxItems);
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_sets_HasMoreRecurringRoots_when_exceeded()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var roots = Enumerable.Range(0, 3)
                .Select(i => CreateRecurringRoot(_userId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _recurringRootRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(roots);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreRecurringRoots.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_sets_HasMoreRecurringSeries_when_exceeded()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var seriesList = Enumerable.Range(0, 3)
                .Select(i => CreateRecurringSeries(_userId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _recurringSeriesRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(seriesList);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreRecurringSeries.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_sets_HasMoreRecurringSeriesSubtasks_when_exceeded()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;
            var seriesId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var subtasks = Enumerable.Range(0, 3)
                .Select(i => CreateRecurringSeriesSubtask(_userId, seriesId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _recurringSeriesSubtaskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subtasks);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreRecurringSeriesSubtasks.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_sets_HasMoreRecurringExceptions_when_exceeded()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;
            var seriesId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            // 3 exceptions using CreateDeletion (simplest factory; IsDeletion=true is valid for bucket tests)
            var exceptions = Enumerable.Range(0, 3)
                .Select(i => CreateRecurringException(_userId, seriesId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _recurringExceptionRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(exceptions);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreRecurringExceptions.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_with_MaxItemsPerEntity_sets_HasMoreRecurringAttachments_when_exceeded()
        {
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            const int maxItems = 2;
            var seriesId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<TaskItem>>(new List<TaskItem>()));
            _noteRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IReadOnlyList<Note>>(new List<Note>()));

            var handler = CreateHandler();

            var attachments = Enumerable.Range(0, 3)
                .Select(i => CreateRecurringAttachment(_userId, seriesId, since.AddMinutes(i + 1), since.AddMinutes(i + 1), isDeleted: false))
                .ToList();

            _recurringAttachmentRepositoryMock
                .Setup(r => r.GetChangedSinceAsync(_userId, since, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachments);

            var result = await handler.Handle(
                new GetSyncChangesQuery(SinceUtc: since, DeviceId: null, MaxItemsPerEntity: maxItems),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.HasMoreRecurringAttachments.Should().BeTrue();
        }

        // -----------------------------------------------------------------------
        // Factory helpers for new entity types
        // -----------------------------------------------------------------------

        private static Subtask CreateSubtask(Guid userId, Guid taskId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = Subtask.Create(userId, taskId, "Item", "a0", createdAt);
            result.IsSuccess.Should().BeTrue();
            var subtask = result.Value!;
            typeof(Subtask).GetProperty(nameof(Subtask.CreatedAtUtc))!.SetValue(subtask, createdAt);
            typeof(Subtask).GetProperty(nameof(Subtask.UpdatedAtUtc))!.SetValue(subtask, updatedAt);
            if (isDeleted) typeof(Subtask).GetProperty(nameof(Subtask.IsDeleted))!.SetValue(subtask, true);
            return subtask;
        }

        private static Attachment CreateAttachment(Guid userId, Guid taskId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = Attachment.Create(Guid.NewGuid(), userId, taskId, "file.pdf", "application/pdf", 1024, "path/blob", 1, createdAt);
            result.IsSuccess.Should().BeTrue();
            var attachment = result.Value!;
            typeof(Attachment).GetProperty(nameof(Attachment.CreatedAtUtc))!.SetValue(attachment, createdAt);
            typeof(Attachment).GetProperty(nameof(Attachment.UpdatedAtUtc))!.SetValue(attachment, updatedAt);
            if (isDeleted) typeof(Attachment).GetProperty(nameof(Attachment.IsDeleted))!.SetValue(attachment, true);
            return attachment;
        }

        private static RecurringTaskRoot CreateRecurringRoot(Guid userId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskRoot.Create(userId, createdAt);
            result.IsSuccess.Should().BeTrue();
            var root = result.Value!;
            typeof(RecurringTaskRoot).GetProperty(nameof(RecurringTaskRoot.CreatedAtUtc))!.SetValue(root, createdAt);
            typeof(RecurringTaskRoot).GetProperty(nameof(RecurringTaskRoot.UpdatedAtUtc))!.SetValue(root, updatedAt);
            if (isDeleted) typeof(RecurringTaskRoot).GetProperty(nameof(RecurringTaskRoot.IsDeleted))!.SetValue(root, true);
            return root;
        }

        private static RecurringTaskSeries CreateRecurringSeries(Guid userId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskSeries.Create(
                userId, Guid.NewGuid(), "FREQ=DAILY",
                new DateOnly(2025, 1, 1), null,
                "Series", null, null, null, null, null, null,
                TaskPriority.Normal, null, null,
                new DateOnly(2025, 1, 1), createdAt);
            result.IsSuccess.Should().BeTrue();
            var series = result.Value!;
            typeof(RecurringTaskSeries).GetProperty(nameof(RecurringTaskSeries.CreatedAtUtc))!.SetValue(series, createdAt);
            typeof(RecurringTaskSeries).GetProperty(nameof(RecurringTaskSeries.UpdatedAtUtc))!.SetValue(series, updatedAt);
            if (isDeleted) typeof(RecurringTaskSeries).GetProperty(nameof(RecurringTaskSeries.IsDeleted))!.SetValue(series, true);
            return series;
        }

        private static RecurringTaskSubtask CreateRecurringSeriesSubtask(Guid userId, Guid seriesId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskSubtask.CreateForSeries(userId, seriesId, "Item", "a0", createdAt);
            result.IsSuccess.Should().BeTrue();
            var subtask = result.Value!;
            typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.CreatedAtUtc))!.SetValue(subtask, createdAt);
            typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.UpdatedAtUtc))!.SetValue(subtask, updatedAt);
            if (isDeleted) typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.IsDeleted))!.SetValue(subtask, true);
            return subtask;
        }

        private static RecurringTaskSubtask CreateRecurringExceptionSubtask(Guid userId, Guid exceptionId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskSubtask.CreateForException(userId, exceptionId, "Item", "a0", false, createdAt);
            result.IsSuccess.Should().BeTrue();
            var subtask = result.Value!;
            typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.CreatedAtUtc))!.SetValue(subtask, createdAt);
            typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.UpdatedAtUtc))!.SetValue(subtask, updatedAt);
            if (isDeleted) typeof(RecurringTaskSubtask).GetProperty(nameof(RecurringTaskSubtask.IsDeleted))!.SetValue(subtask, true);
            return subtask;
        }

        private static RecurringTaskException CreateRecurringException(Guid userId, Guid seriesId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskException.CreateDeletion(
                userId, seriesId, new DateOnly(2025, 2, 1), null, createdAt);
            result.IsSuccess.Should().BeTrue();
            var exception = result.Value!;
            typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.CreatedAtUtc))!.SetValue(exception, createdAt);
            typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.UpdatedAtUtc))!.SetValue(exception, updatedAt);
            if (isDeleted) typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.IsDeleted))!.SetValue(exception, true);
            return exception;
        }

        private static RecurringTaskException CreateRecurringOverrideException(Guid userId, Guid seriesId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            // IsDeletion == false; eligible for subtask inlining on initial sync.
            var result = RecurringTaskException.CreateOverride(
                userId, seriesId, new DateOnly(2025, 2, 1),
                null, null, null, null, null, null, null, null, null, null, null,
                false, null, createdAt);
            result.IsSuccess.Should().BeTrue();
            var exception = result.Value!;
            typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.CreatedAtUtc))!.SetValue(exception, createdAt);
            typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.UpdatedAtUtc))!.SetValue(exception, updatedAt);
            if (isDeleted) typeof(RecurringTaskException).GetProperty(nameof(RecurringTaskException.IsDeleted))!.SetValue(exception, true);
            return exception;
        }

        private static RecurringTaskAttachment CreateRecurringAttachment(Guid userId, Guid seriesId, DateTime createdAt, DateTime updatedAt, bool isDeleted)
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                Guid.NewGuid(), userId, seriesId, "file.pdf", "application/pdf", 1024, "path/blob", 1, createdAt);
            result.IsSuccess.Should().BeTrue();
            var attachment = result.Value!;
            typeof(RecurringTaskAttachment).GetProperty(nameof(RecurringTaskAttachment.CreatedAtUtc))!.SetValue(attachment, createdAt);
            typeof(RecurringTaskAttachment).GetProperty(nameof(RecurringTaskAttachment.UpdatedAtUtc))!.SetValue(attachment, updatedAt);
            if (isDeleted) typeof(RecurringTaskAttachment).GetProperty(nameof(RecurringTaskAttachment.IsDeleted))!.SetValue(attachment, true);
            return attachment;
        }
    }
}
