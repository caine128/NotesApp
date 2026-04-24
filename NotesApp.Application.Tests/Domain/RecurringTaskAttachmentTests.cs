using FluentAssertions;
using NotesApp.Domain.Entities;
using System;

namespace NotesApp.Application.Tests.Domain
{
    /// <summary>
    /// Unit tests for the RecurringTaskAttachment domain entity.
    ///
    /// Covers:
    /// - CreateForSeries happy path — all properties set correctly.
    /// - CreateForException happy path — all properties set correctly.
    /// - Dual-FK invariant: exactly one of SeriesId / ExceptionId is non-null.
    /// - Whitespace trimming on FileName.
    /// - Null/empty contentType normalisation to "application/octet-stream".
    /// - Each invalid field produces the expected error code.
    /// - Multiple invalid fields produce all errors.
    /// - SoftDelete marks entity deleted.
    /// - SoftDelete is idempotent (no Version — attachment is immutable).
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class RecurringTaskAttachmentTests
    {
        private readonly DateTime _now = new(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly Guid _id = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _seriesId = Guid.NewGuid();
        private readonly Guid _exceptionId = Guid.NewGuid();

        // ── CreateForSeries ───────────────────────────────────────────────────

        [Fact]
        public void CreateForSeries_with_valid_input_returns_success_and_sets_all_properties()
        {
            var blobPath = $"{_userId}/recurring-series-attachments/{_seriesId}/{_id}/report.pdf";

            var result = RecurringTaskAttachment.CreateForSeries(
                id: _id,
                userId: _userId,
                seriesId: _seriesId,
                fileName: "report.pdf",
                contentType: "application/pdf",
                sizeBytes: 2048,
                blobPath: blobPath,
                displayOrder: 1,
                utcNow: _now);

            result.IsSuccess.Should().BeTrue();
            var attachment = result.Value!;
            attachment.Id.Should().Be(_id);
            attachment.UserId.Should().Be(_userId);
            attachment.SeriesId.Should().Be(_seriesId);
            attachment.ExceptionId.Should().BeNull();
            attachment.FileName.Should().Be("report.pdf");
            attachment.ContentType.Should().Be("application/pdf");
            attachment.SizeBytes.Should().Be(2048);
            attachment.BlobPath.Should().Be(blobPath);
            attachment.DisplayOrder.Should().Be(1);
            attachment.IsDeleted.Should().BeFalse();
            attachment.CreatedAtUtc.Should().Be(_now);
            attachment.UpdatedAtUtc.Should().Be(_now);
        }

        // ── CreateForException ────────────────────────────────────────────────

        [Fact]
        public void CreateForException_with_valid_input_returns_success_and_sets_all_properties()
        {
            var blobPath = $"{_userId}/recurring-exception-attachments/{_exceptionId}/{_id}/photo.jpg";

            var result = RecurringTaskAttachment.CreateForException(
                id: _id,
                userId: _userId,
                exceptionId: _exceptionId,
                fileName: "photo.jpg",
                contentType: "image/jpeg",
                sizeBytes: 512000,
                blobPath: blobPath,
                displayOrder: 2,
                utcNow: _now);

            result.IsSuccess.Should().BeTrue();
            var attachment = result.Value!;
            attachment.Id.Should().Be(_id);
            attachment.UserId.Should().Be(_userId);
            attachment.SeriesId.Should().BeNull();
            attachment.ExceptionId.Should().Be(_exceptionId);
            attachment.FileName.Should().Be("photo.jpg");
            attachment.ContentType.Should().Be("image/jpeg");
            attachment.SizeBytes.Should().Be(512000);
            attachment.DisplayOrder.Should().Be(2);
            attachment.IsDeleted.Should().BeFalse();
        }

        // ── Dual-FK invariant ─────────────────────────────────────────────────

        [Fact]
        public void CreateForSeries_sets_SeriesId_and_leaves_ExceptionId_null()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.SeriesId.Should().NotBeNull();
            result.Value!.ExceptionId.Should().BeNull();
        }

        [Fact]
        public void CreateForException_sets_ExceptionId_and_leaves_SeriesId_null()
        {
            var result = RecurringTaskAttachment.CreateForException(
                _id, _userId, _exceptionId, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.ExceptionId.Should().NotBeNull();
            result.Value!.SeriesId.Should().BeNull();
        }

        // ── Whitespace and normalisation ──────────────────────────────────────

        [Fact]
        public void CreateForSeries_trims_whitespace_from_fileName()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "  report.pdf  ", "application/pdf", 1024, "path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.FileName.Should().Be("report.pdf");
        }

        [Fact]
        public void CreateForSeries_with_null_contentType_normalises_to_octet_stream()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.bin", null, 1024, "path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.ContentType.Should().Be("application/octet-stream");
        }

        [Fact]
        public void CreateForSeries_with_whitespace_contentType_normalises_to_octet_stream()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.bin", "   ", 1024, "path", 1, _now);

            result.IsSuccess.Should().BeTrue();
            result.Value!.ContentType.Should().Be("application/octet-stream");
        }

        // ── Validation failures ────────────────────────────────────────────────

        [Fact]
        public void CreateForSeries_with_empty_id_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                Guid.Empty, _userId, _seriesId, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.Id.Empty");
        }

        [Fact]
        public void CreateForSeries_with_empty_userId_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, Guid.Empty, _seriesId, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.UserId.Empty");
        }

        [Fact]
        public void CreateForSeries_with_empty_seriesId_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, Guid.Empty, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.SeriesId.Empty");
        }

        [Fact]
        public void CreateForException_with_empty_exceptionId_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForException(
                _id, _userId, Guid.Empty, "file.pdf", "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.ExceptionId.Empty");
        }

        [Fact]
        public void CreateForSeries_with_null_fileName_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, null, "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.FileName.Empty");
        }

        [Fact]
        public void CreateForSeries_with_whitespace_only_fileName_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "   ", "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.FileName.Empty");
        }

        [Fact]
        public void CreateForSeries_with_fileName_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', RecurringTaskAttachment.MaxFileNameLength + 1);

            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, tooLong, "application/pdf", 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.FileName.TooLong");
        }

        [Fact]
        public void CreateForSeries_with_contentType_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', RecurringTaskAttachment.MaxContentTypeLength + 1);

            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", tooLong, 1024, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.ContentType.TooLong");
        }

        [Fact]
        public void CreateForSeries_with_empty_blobPath_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 1024, "", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.BlobPath.Empty");
        }

        [Fact]
        public void CreateForSeries_with_blobPath_exceeding_max_length_returns_failure()
        {
            var tooLong = new string('a', RecurringTaskAttachment.MaxBlobPathLength + 1);

            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 1024, tooLong, 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.BlobPath.TooLong");
        }

        [Fact]
        public void CreateForSeries_with_zero_sizeBytes_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 0, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.SizeBytes.Invalid");
        }

        [Fact]
        public void CreateForSeries_with_negative_sizeBytes_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", -1, "path", 1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.SizeBytes.Invalid");
        }

        [Fact]
        public void CreateForSeries_with_zero_displayOrder_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 1024, "path", 0, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.DisplayOrder.Invalid");
        }

        [Fact]
        public void CreateForSeries_with_negative_displayOrder_returns_failure()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "file.pdf", "application/pdf", 1024, "path", -1, _now);

            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "RecurringAttachment.DisplayOrder.Invalid");
        }

        [Fact]
        public void CreateForSeries_with_multiple_invalid_fields_returns_all_errors()
        {
            var result = RecurringTaskAttachment.CreateForSeries(
                Guid.Empty, Guid.Empty, Guid.Empty, null, "application/pdf", 0, "", 0, _now);

            result.IsFailure.Should().BeTrue();
            // Expect at least: Id.Empty, UserId.Empty, SeriesId.Empty, FileName.Empty,
            // SizeBytes.Invalid, BlobPath.Empty, DisplayOrder.Invalid = 7 errors
            result.Errors.Should().HaveCountGreaterThanOrEqualTo(7);
        }

        // ── SoftDelete ────────────────────────────────────────────────────────

        [Fact]
        public void SoftDelete_marks_entity_as_deleted_and_updates_timestamp()
        {
            var attachment = CreateValidSeriesAttachment();
            var deleteTime = _now.AddMinutes(10);

            var result = attachment.SoftDelete(deleteTime);

            result.IsSuccess.Should().BeTrue();
            attachment.IsDeleted.Should().BeTrue();
            attachment.UpdatedAtUtc.Should().Be(deleteTime);
        }

        [Fact]
        public void SoftDelete_is_idempotent_when_already_deleted()
        {
            // RecurringTaskAttachment has no Version; calling SoftDelete twice must not throw
            // and must still return Success (idempotent).
            var attachment = CreateValidSeriesAttachment();
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

        private RecurringTaskAttachment CreateValidSeriesAttachment()
        {
            return RecurringTaskAttachment.CreateForSeries(
                _id, _userId, _seriesId, "report.pdf", "application/pdf", 1024,
                $"{_userId}/recurring-series-attachments/{_seriesId}/{_id}/report.pdf",
                1, _now).Value!;
        }
    }
}
