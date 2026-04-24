using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A file attached to a recurring task series or a specific occurrence exception.
    ///
    /// Serves dual purpose via two nullable FKs:
    /// - SeriesId set, ExceptionId null  → series template attachment (inherited by all occurrences by default)
    /// - ExceptionId set, SeriesId null  → exception attachment override (for one specific occurrence)
    ///
    /// Attachment resolution rule: exception exists AND <see cref="RecurringTaskException.HasAttachmentOverride"/> = true
    /// → use exception attachments; otherwise → use series template attachments.
    ///
    /// Immutable after creation: no updates allowed, only create or soft-delete.
    /// Blobs shared by ThisAndFollowing copies (same BlobPath, different IDs) are only removed by the
    /// orphan-cleanup worker once no non-deleted RecurringTaskAttachment references the path.
    ///
    /// Invariants:
    /// - Exactly one of SeriesId / ExceptionId is non-null (factories + DB check constraint).
    /// - UserId must be non-empty.
    /// - FileName must be non-empty.
    /// - BlobPath must be non-empty.
    /// - SizeBytes must be positive.
    /// - DisplayOrder must be at least 1.
    /// </summary>
    public sealed class RecurringTaskAttachment : Entity<Guid>, ISyncableEntity
    {
        // ENTITY CONSTANTS

        /// <summary>Maximum character length for the original filename.</summary>
        public const int MaxFileNameLength = 256;

        /// <summary>Maximum character length for the MIME content type.</summary>
        public const int MaxContentTypeLength = 100;

        /// <summary>Maximum character length for the blob storage path.</summary>
        public const int MaxBlobPathLength = 500;

        // PROPERTIES

        /// <inheritdoc />
        public Guid UserId { get; private set; }

        /// <summary>
        /// FK to RecurringTaskSeries. Set when this is a series template attachment.
        /// Null when this is an exception attachment override.
        /// </summary>
        public Guid? SeriesId { get; private set; }

        /// <summary>
        /// FK to RecurringTaskException. Set when this is an exception attachment override.
        /// Null when this is a series template attachment.
        /// </summary>
        public Guid? ExceptionId { get; private set; }

        /// <summary>Original filename from the client (e.g. "report.pdf").</summary>
        public string FileName { get; private set; } = string.Empty;

        /// <summary>MIME type (e.g. "image/jpeg", "application/pdf").</summary>
        public string ContentType { get; private set; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; private set; }

        /// <summary>
        /// Path within blob storage.
        /// Series format:    {userId}/recurring-series-attachments/{seriesId}/{attachmentId}/{sanitizedFileName}
        /// Exception format: {userId}/recurring-exception-attachments/{exceptionId}/{attachmentId}/{sanitizedFileName}
        /// </summary>
        public string BlobPath { get; private set; } = string.Empty;

        /// <summary>1-based upload order — server-assigned at creation time.</summary>
        public int DisplayOrder { get; private set; }

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private RecurringTaskAttachment() { }

        private RecurringTaskAttachment(Guid id,
                                        Guid userId,
                                        Guid? seriesId,
                                        Guid? exceptionId,
                                        string fileName,
                                        string contentType,
                                        long sizeBytes,
                                        string blobPath,
                                        int displayOrder,
                                        DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            SeriesId = seriesId;
            ExceptionId = exceptionId;
            FileName = fileName;
            ContentType = contentType;
            SizeBytes = sizeBytes;
            BlobPath = blobPath;
            DisplayOrder = displayOrder;
        }

        // FACTORIES

        /// <summary>
        /// Creates a new series template attachment linked to a <see cref="RecurringTaskSeries"/>.
        /// </summary>
        /// <param name="id">Pre-generated attachment ID — embed in blob path before calling.</param>
        public static DomainResult<RecurringTaskAttachment> CreateForSeries(
            Guid id,
            Guid userId,
            Guid seriesId,
            string? fileName,
            string? contentType,
            long sizeBytes,
            string blobPath,
            int displayOrder,
            DateTime utcNow)
        {
            return Create(id, userId, seriesId: seriesId, exceptionId: null,
                          fileName, contentType, sizeBytes, blobPath, displayOrder, utcNow);
        }

        /// <summary>
        /// Creates a new exception attachment override linked to a <see cref="RecurringTaskException"/>.
        /// </summary>
        /// <param name="id">Pre-generated attachment ID — embed in blob path before calling.</param>
        public static DomainResult<RecurringTaskAttachment> CreateForException(
            Guid id,
            Guid userId,
            Guid exceptionId,
            string? fileName,
            string? contentType,
            long sizeBytes,
            string blobPath,
            int displayOrder,
            DateTime utcNow)
        {
            return Create(id, userId, seriesId: null, exceptionId: exceptionId,
                          fileName, contentType, sizeBytes, blobPath, displayOrder, utcNow);
        }

        // BEHAVIOURS

        /// <summary>
        /// Soft-deletes this attachment.
        /// Idempotent: soft-deleting an already-deleted attachment returns success.
        ///
        /// The blob in storage is cleaned up separately by the background orphan-cleanup worker,
        /// which checks that no other non-deleted RecurringTaskAttachment shares the same BlobPath
        /// before deleting the blob (handles ThisAndFollowing shared-blob scenarios).
        /// </summary>
        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Success();

            MarkDeleted(utcNow);
            return DomainResult.Success();
        }

        // PRIVATE HELPERS

        private static DomainResult<RecurringTaskAttachment> Create(
            Guid id,
            Guid userId,
            Guid? seriesId,
            Guid? exceptionId,
            string? fileName,
            string? contentType,
            long sizeBytes,
            string blobPath,
            int displayOrder,
            DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedFileName = fileName?.Trim() ?? string.Empty;
            var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim();
            var normalizedBlobPath = blobPath?.Trim() ?? string.Empty;

            if (id == Guid.Empty)
                errors.Add(new DomainError("RecurringAttachment.Id.Empty", "Id must be a non-empty GUID."));

            if (userId == Guid.Empty)
                errors.Add(new DomainError("RecurringAttachment.UserId.Empty", "UserId must be a non-empty GUID."));

            if (seriesId.HasValue && seriesId.Value == Guid.Empty)
                errors.Add(new DomainError("RecurringAttachment.SeriesId.Empty", "SeriesId must be a non-empty GUID."));

            if (exceptionId.HasValue && exceptionId.Value == Guid.Empty)
                errors.Add(new DomainError("RecurringAttachment.ExceptionId.Empty", "ExceptionId must be a non-empty GUID."));

            if (string.IsNullOrWhiteSpace(normalizedFileName))
                errors.Add(new DomainError("RecurringAttachment.FileName.Empty", "FileName is required."));

            if (normalizedFileName.Length > MaxFileNameLength)
                errors.Add(new DomainError("RecurringAttachment.FileName.TooLong",
                    $"FileName must be at most {MaxFileNameLength} characters."));

            if (normalizedContentType.Length > MaxContentTypeLength)
                errors.Add(new DomainError("RecurringAttachment.ContentType.TooLong",
                    $"ContentType must be at most {MaxContentTypeLength} characters."));

            if (string.IsNullOrWhiteSpace(normalizedBlobPath))
                errors.Add(new DomainError("RecurringAttachment.BlobPath.Empty", "BlobPath is required."));

            if (normalizedBlobPath.Length > MaxBlobPathLength)
                errors.Add(new DomainError("RecurringAttachment.BlobPath.TooLong",
                    $"BlobPath must be at most {MaxBlobPathLength} characters."));

            if (sizeBytes <= 0)
                errors.Add(new DomainError("RecurringAttachment.SizeBytes.Invalid",
                    "SizeBytes must be a positive number."));

            if (displayOrder < 1)
                errors.Add(new DomainError("RecurringAttachment.DisplayOrder.Invalid",
                    "DisplayOrder must be at least 1."));

            if (errors.Count > 0)
                return DomainResult<RecurringTaskAttachment>.Failure(errors);

            return DomainResult<RecurringTaskAttachment>.Success(
                new RecurringTaskAttachment(
                    id: id,
                    userId: userId,
                    seriesId: seriesId,
                    exceptionId: exceptionId,
                    fileName: normalizedFileName,
                    contentType: normalizedContentType,
                    sizeBytes: sizeBytes,
                    blobPath: normalizedBlobPath,
                    displayOrder: displayOrder,
                    utcNow: utcNow));
        }
    }
}
