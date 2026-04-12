using FluentAssertions;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Domain
{
    /// <summary>
    /// Unit tests for the Attachment domain entity.
    ///
    /// Covers:
    /// - Create happy path — all properties are set correctly.
    /// - Whitespace trimming on FileName.
    /// - Null/empty contentType normalisation to "application/octet-stream".
    /// - Each invalid field produces the expected error code.
    /// - Multiple invalid fields produce all errors.
    /// - SoftDelete marks entity deleted.
    /// - SoftDelete is idempotent (does not increment version because Attachment has no Version).
    /// </summary>
    public sealed class AttachmentTests
    {
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly Guid _id = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _taskId = Guid.NewGuid();

        // ── Create ────────────────────────────────────────────────────────────

        [Fact]
        public void Create_with_valid_input_returns_success_and_sets_all_properties()
        {
            var result = Attachment.Create(
                id: _id,
                userId: _userId,
                taskId: _taskId,
                fileName: "report.pdf",
                contentType: "application/pdf",
                sizeBytes: 1024,
                blobPath: $"{_userId}/task-attachments/{_taskId}/{_id}/report.pdf",
                displayOrder: 1,
                utcNow: _now);

            result.IsSuccess.Should().BeTrue();
            var attachment = result.Value!;
            attachment.Id.Should().Be(_id);
            attachment.UserId.Should().Be(_userId);
            attachment.TaskId.Should().Be(_taskId);
            attachment.FileName.Should().Be("report.pdf");
            attachment.ContentType.Should().Be("application/pdf");
            attachment.SizeBytes.Should().Be(1024);
            attachment.DisplayOrder.Should().Be(1);
            attachment.IsDeleted.Should().BeFalse();
            attachment.CreatedAtUtc.Should().Be(_now);
            attachment.UpdatedAtUtc.Should().Be(_now);
        }

        [Fact]
        public void Create_trims_whitespace_from_fileName()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "  report.pdf  ", "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.FileName.Should().Be("report.pdf");
        }

        [Fact]
        public void Create_with_null_contentType_normalises_to_octet_stream()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.bin", null, 1024,
                "some/path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.ContentType.Should().Be("application/octet-stream");
        }

        [Fact]
        public void Create_with_empty_contentType_normalises_to_octet_stream()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.bin", "   ", 1024,
                "some/path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.ContentType.Should().Be("application/octet-stream");
        }

        [Fact]
        public void Create_with_empty_id_returns_failure()
        {
            var result = Attachment.Create(
                Guid.Empty, _userId, _taskId,
                "file.pdf", "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.Id.Empty");
        }

        [Fact]
        public void Create_with_empty_userId_returns_failure()
        {
            var result = Attachment.Create(
                _id, Guid.Empty, _taskId,
                "file.pdf", "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.UserId.Empty");
        }

        [Fact]
        public void Create_with_empty_taskId_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, Guid.Empty,
                "file.pdf", "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.TaskId.Empty");
        }

        [Fact]
        public void Create_with_null_fileName_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                null, "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.FileName.Empty");
        }

        [Fact]
        public void Create_with_whitespace_only_fileName_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "   ", "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.FileName.Empty");
        }

        [Fact]
        public void Create_with_fileName_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', Attachment.MaxFileNameLength + 1);

            var result = Attachment.Create(
                _id, _userId, _taskId,
                tooLong, "application/pdf", 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.FileName.TooLong");
        }

        [Fact]
        public void Create_with_contentType_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', Attachment.MaxContentTypeLength + 1);

            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", tooLong, 1024,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.ContentType.TooLong");
        }

        [Fact]
        public void Create_with_empty_blobPath_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", 1024,
                "", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.BlobPath.Empty");
        }

        [Fact]
        public void Create_with_blobPath_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', Attachment.MaxBlobPathLength + 1);

            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", 1024,
                tooLong, 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.BlobPath.TooLong");
        }

        [Fact]
        public void Create_with_zero_sizeBytes_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", 0,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.SizeBytes.Invalid");
        }

        [Fact]
        public void Create_with_negative_sizeBytes_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", -1,
                "some/path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.SizeBytes.Invalid");
        }

        [Fact]
        public void Create_with_zero_displayOrder_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", 1024,
                "some/path", 0, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.DisplayOrder.Invalid");
        }

        [Fact]
        public void Create_with_negative_displayOrder_returns_failure()
        {
            var result = Attachment.Create(
                _id, _userId, _taskId,
                "file.pdf", "application/pdf", 1024,
                "some/path", -1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "Attachment.DisplayOrder.Invalid");
        }

        [Fact]
        public void Create_with_multiple_invalid_fields_returns_all_errors()
        {
            var result = Attachment.Create(
                Guid.Empty, Guid.Empty, Guid.Empty,
                null, "application/pdf", 0,
                "", 0, _now);

            result.IsFailure.Should().BeTrue();
            // Expect at least: Id.Empty, UserId.Empty, TaskId.Empty, FileName.Empty,
            // SizeBytes.Invalid, BlobPath.Empty, DisplayOrder.Invalid = 7 errors
            result.Errors.Should().HaveCountGreaterThanOrEqualTo(7);
        }

        // ── SoftDelete ────────────────────────────────────────────────────────

        [Fact]
        public void SoftDelete_marks_entity_as_deleted_and_updates_timestamp()
        {
            var attachment = CreateValidAttachment();
            var deleteTime = _now.AddMinutes(10);

            var result = attachment.SoftDelete(deleteTime);

            result.IsSuccess.Should().BeTrue();
            attachment.IsDeleted.Should().BeTrue();
            attachment.UpdatedAtUtc.Should().Be(deleteTime);
        }

        [Fact]
        public void SoftDelete_is_idempotent_when_already_deleted()
        {
            // Attachment has no Version; calling SoftDelete twice must not throw
            // and must still return Success (idempotent).
            var attachment = CreateValidAttachment();
            attachment.SoftDelete(_now.AddMinutes(5));
            var updatedAtAfterFirstDelete = attachment.UpdatedAtUtc;

            var secondResult = attachment.SoftDelete(_now.AddMinutes(10));

            secondResult.IsSuccess.Should().BeTrue();
            attachment.IsDeleted.Should().BeTrue();
            // UpdatedAtUtc must NOT change on the idempotent call.
            attachment.UpdatedAtUtc.Should().Be(updatedAtAfterFirstDelete,
                "idempotent soft-delete must not update the timestamp again");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private Attachment CreateValidAttachment()
        {
            return Attachment.Create(
                _id, _userId, _taskId,
                "report.pdf", "application/pdf", 1024,
                $"{_userId}/task-attachments/{_taskId}/{_id}/report.pdf",
                1, _now).Value!;
        }
    }
}
