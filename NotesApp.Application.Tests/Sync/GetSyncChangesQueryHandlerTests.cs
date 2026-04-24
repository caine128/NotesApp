using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
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
        private readonly Mock<ILogger<GetSyncChangesQueryHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();

        private GetSyncChangesQueryHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

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
    }
}
