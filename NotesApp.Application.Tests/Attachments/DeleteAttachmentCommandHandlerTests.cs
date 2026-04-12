using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Attachments.Commands.DeleteAttachment;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Attachments
{
    /// <summary>
    /// Unit tests for DeleteAttachmentCommandHandler.
    ///
    /// Covers:
    /// - Happy path: attachment found, owned by current user, soft-deleted, outbox emitted.
    /// - Not found: null from repository → NotFound error.
    /// - Wrong user: attachment belongs to different user → NotFound error (no info leakage).
    /// - Already deleted: global query filter hides deleted attachments → NotFound (idempotent).
    ///   (GetByIdUntrackedAsync returns null for soft-deleted entities due to the global filter.)
    /// </summary>
    public sealed class DeleteAttachmentCommandHandlerTests
    {
        private readonly Mock<IAttachmentRepository> _attachmentRepositoryMock = new();
        private readonly Mock<IOutboxRepository> _outboxRepositoryMock = new();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
        private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
        private readonly Mock<ISystemClock> _clockMock = new();
        private readonly Mock<ILogger<DeleteAttachmentCommandHandler>> _loggerMock = new();

        private readonly Guid _userId = Guid.NewGuid();
        private readonly DateTime _now = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        private DeleteAttachmentCommandHandler CreateHandler()
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

            return new DeleteAttachmentCommandHandler(
                _attachmentRepositoryMock.Object,
                _outboxRepositoryMock.Object,
                _unitOfWorkMock.Object,
                _currentUserServiceMock.Object,
                _clockMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task Handle_happy_path_soft_deletes_attachment_emits_outbox_and_saves()
        {
            var handler = CreateHandler();
            var attachmentId = Guid.NewGuid();
            var attachment = CreateAttachment(_userId, attachmentId);

            _attachmentRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(attachmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(attachment);

            var result = await handler.Handle(
                new DeleteAttachmentCommand { AttachmentId = attachmentId }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            _attachmentRepositoryMock.Verify(r => r.Update(It.IsAny<Attachment>()), Times.Once);
            _outboxRepositoryMock.Verify(
                r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Handle_attachment_not_found_returns_not_found_error()
        {
            var handler = CreateHandler();
            var attachmentId = Guid.NewGuid();

            _attachmentRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(attachmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Attachment?)null);

            var result = await handler.Handle(
                new DeleteAttachmentCommand { AttachmentId = attachmentId }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Attachments.NotFound");

            _attachmentRepositoryMock.Verify(r => r.Update(It.IsAny<Attachment>()), Times.Never);
            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_attachment_belongs_to_different_user_returns_not_found_error()
        {
            var handler = CreateHandler();
            var attachmentId = Guid.NewGuid();
            var foreignAttachment = CreateAttachment(Guid.NewGuid(), attachmentId); // different user

            _attachmentRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(attachmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(foreignAttachment);

            var result = await handler.Handle(
                new DeleteAttachmentCommand { AttachmentId = attachmentId }, CancellationToken.None);

            result.IsFailed.Should().BeTrue();
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Attachments.NotFound");

            _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Handle_already_deleted_attachment_returns_not_found_because_global_filter_hides_it()
        {
            // The global query filter [!IsDeleted] means GetByIdUntrackedAsync returns null
            // for a soft-deleted attachment, so the handler returns NotFound (idempotent safe retry).
            var handler = CreateHandler();
            var attachmentId = Guid.NewGuid();

            // Simulate the global filter: soft-deleted attachment is invisible → null returned.
            _attachmentRepositoryMock
                .Setup(r => r.GetByIdUntrackedAsync(attachmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Attachment?)null);

            var result = await handler.Handle(
                new DeleteAttachmentCommand { AttachmentId = attachmentId }, CancellationToken.None);

            result.IsFailed.Should().BeTrue("a deleted attachment is invisible to GetByIdUntrackedAsync");
            result.Errors.Should().Contain(e =>
                e.Metadata.ContainsKey("ErrorCode") &&
                e.Metadata["ErrorCode"].ToString() == "Attachments.NotFound");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Attachment CreateAttachment(Guid userId, Guid attachmentId)
        {
            var id = attachmentId == Guid.Empty ? Guid.NewGuid() : attachmentId;
            var taskId = Guid.NewGuid();

            var result = Attachment.Create(
                id, userId, taskId,
                "report.pdf", "application/pdf", 1024,
                $"{userId}/task-attachments/{taskId}/{id}/report.pdf",
                1,
                new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc));

            result.IsSuccess.Should().BeTrue("test helper must produce a valid Attachment");
            return result.Value!;
        }
    }
}
