using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Attachments.Commands.UploadAttachment;
using NotesApp.Application.Attachments.Models;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.IO;

namespace NotesApp.Application.Tests.Attachments
{
    /// <summary>
    /// Unit tests for UploadAttachmentCommandHandler.
    ///
    /// Covers:
    /// - Happy path: task found, content type allowed, limit not exceeded, blob uploaded,
    ///   entity created, outbox emitted, download URL returned.
    /// - Task not found / wrong user → fail before blob upload.
    /// - Disallowed content type → fail before blob upload.
    /// - MaxAttachmentsPerTask exceeded → fail before blob upload.
    /// - Blob upload failure → fail, no DB changes.
    /// - Download URL generation failure → success with null DownloadUrl (best-effort).
    /// - DisplayOrder is set to existingCount + 1.
    /// </summary>
    public sealed class UploadAttachmentCommandHandlerTests
    {
        private readonly Mock<ITaskRepository> _taskRepositoryMock = new();
        private readonly Mock<IAttachmentRepository> _attachmentRepositoryMock = new();
        private readonly Mock<IBlobStorageService> _blobStorageServiceMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<UploadAttachmentCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        private readonly AttachmentStorageOptions _options = new()
        {
            ContainerName = "user-attachments",
            DownloadUrlValidityMinutes = 60,
            MaxFileSizeBytes = 50 * 1024 * 1024,
            MaxAttachmentsPerTask = 5,
            AllowedContentTypes = new[] { "image/jpeg", "image/png", "application/pdf" }
        };

        private UploadAttachmentCommandHandler CreateHandler()
        {
            _currentUserServiceMock
                .Setup(s => s.GetUserIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_userId);

            _clockMock
                .Setup(c => c.UtcNow)
                .Returns(_now);

            _unitOfWorkMock
                .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return new UploadAttachmentCommandHandler(
                _taskRepositoryMock.Object,
                _attachmentRepositoryMock.Object,
                _blobStorageServiceMock.Object,
                _currentUserServiceMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _clockMock.Object,
                Options.Create(_options),
                _loggerMock.Object);
        }

        // ── Happy path ────────────────────────────────────────────────────────

        [Fact]
        public async Task Handle_happy_path_uploads_blob_persists_entity_and_returns_download_url()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);
            const string downloadUrl = "https://storage.example.com/blob?sas=token";

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _blobStorageServiceMock
                .Setup(s => s.UploadAsync(
                    _options.ContainerName,
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(new StorageUploadResult("path", "application/pdf", 1024, "etag")));

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(downloadUrl));

            var command = new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "report.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            };

            var result = await handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            var dto = result.Value!;
            dto.TaskId.Should().Be(taskId);
            dto.AttachmentId.Should().NotBe(Guid.Empty);
            dto.DisplayOrder.Should().Be(1);
            dto.DownloadUrl.Should().Be(downloadUrl);

            _attachmentRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Once);
            _outboxRepositoryMock.Verify(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_assigns_display_order_as_existing_count_plus_one()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            // 3 existing attachments → displayOrder should be 4
            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            _blobStorageServiceMock
                .Setup(s => s.UploadAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(new StorageUploadResult("path", "application/pdf", 1024, "etag")));

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok("https://url"));

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "photo.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 512
            }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value!.DisplayOrder.Should().Be(4);
        }

        // ── Task not found / wrong user ───────────────────────────────────────

        [Fact]
        public async Task Handle_task_not_found_returns_failure_without_touching_blob_storage()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskItem?)null);

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "file.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Tasks.NotFound");

            _blobStorageServiceMock.Verify(
                s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_task_belongs_to_different_user_returns_failure()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(Guid.NewGuid(), taskId); // different user

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "file.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Tasks.NotFound");

            _blobStorageServiceMock.Verify(
                s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Disallowed content type ───────────────────────────────────────────

        [Fact]
        public async Task Handle_disallowed_content_type_returns_failure_without_touching_blob_storage()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "script.exe",
                ContentType = "application/octet-stream", // not in AllowedContentTypes
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Attachment.ContentType.NotAllowed");

            _blobStorageServiceMock.Verify(
                s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Handle_content_type_check_is_case_insensitive()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _blobStorageServiceMock
                .Setup(s => s.UploadAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(new StorageUploadResult("path", "application/pdf", 1024, "etag")));

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok("https://url"));

            // "APPLICATION/PDF" matches "application/pdf" in AllowedContentTypes
            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "report.pdf",
                ContentType = "APPLICATION/PDF",
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue("content-type matching must be case-insensitive");
        }

        // ── MaxAttachmentsPerTask exceeded ────────────────────────────────────

        [Fact]
        public async Task Handle_max_attachments_exceeded_returns_failure()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            // Already at the limit
            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_options.MaxAttachmentsPerTask);

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "extra.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Attachment.LimitExceeded");

            _blobStorageServiceMock.Verify(
                s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Blob upload failure ───────────────────────────────────────────────

        [Fact]
        public async Task Handle_blob_upload_failure_returns_failure_and_no_db_changes()
        {
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _blobStorageServiceMock
                .Setup(s => s.UploadAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Fail("Storage unavailable"));

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "file.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Message == "Attachment.Upload.Failed");

            _attachmentRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Attachment>(), It.IsAny<CancellationToken>()), Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── Download URL generation failure (best-effort) ─────────────────────

        [Fact]
        public async Task Handle_url_generation_failure_returns_success_with_null_download_url()
        {
            // URL generation is best-effort; the upload is already committed to DB.
            // If URL generation fails, the handler returns Ok with DownloadUrl = null.
            var handler = CreateHandler();
            var taskId = Guid.NewGuid();
            var task = CreateTask(_userId, taskId);

            _taskRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(taskId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(task);

            _attachmentRepositoryMock
                .Setup(r => r.CountForTaskAsync(taskId, _userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            _blobStorageServiceMock
                .Setup(s => s.UploadAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok(new StorageUploadResult("path", "application/pdf", 1024, "etag")));

            _blobStorageServiceMock
                .Setup(s => s.GenerateDownloadUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Fail("URL service unavailable"));

            var result = await handler.Handle(new UploadAttachmentCommand
            {
                TaskId = taskId,
                Content = Stream.Null,
                FileName = "file.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024
            }, CancellationToken.None);

            // Still success — upload committed
            result.IsSuccess.Should().BeTrue("URL generation failure must not fail the overall upload");
            result.Value!.DownloadUrl.Should().BeNull("URL generation was best-effort and failed");

            // DB changes must have been persisted
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static TaskItem CreateTask(Guid userId, Guid taskId)
        {
            var result = TaskItem.Create(
                userId,
                new DateOnly(2025, 6, 1),
                "Test task",
                null, null, null, null, null, null,
                TaskPriority.Normal,
                new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc));

            result.IsSuccess.Should().BeTrue("test helper must produce a valid TaskItem");
            var task = result.Value!;

            // Set the Id via reflection to control it in tests
            typeof(TaskItem).GetProperty("Id")!.SetValue(task, taskId);

            return task;
        }
    }
}
