using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Represents a file attached to a <see cref="TaskItem"/>.
    ///
    /// Models email-style attachments: the file belongs to the task as a whole,
    /// not to any specific position in the text (no block/position logic).
    ///
    /// Immutable after creation: no updates allowed, only create or soft-delete.
    /// This simplifies sync (no version tracking needed) and blob management.
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// - TaskId must be non-empty.
    /// - FileName must be non-empty.
    /// - BlobPath must be non-empty.
    /// - SizeBytes must be positive.
    /// - DisplayOrder must be at least 1 (1-based upload order, server-assigned).
    /// </summary>
    public sealed class Attachment : Entity<Guid>, ISyncableEntity
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

        /// <summary>The parent task this attachment belongs to.</summary>
        public Guid TaskId { get; private set; }

        /// <summary>Original filename from the client (e.g. "report.pdf").</summary>
        public string FileName { get; private set; } = string.Empty;

        /// <summary>MIME type (e.g. "image/jpeg", "application/pdf").</summary>
        public string ContentType { get; private set; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; private set; }

        /// <summary>
        /// Path within blob storage.
        /// Format: {userId}/task-attachments/{taskId}/{attachmentId}/{sanitizedFileName}
        /// </summary>
        public string BlobPath { get; private set; } = string.Empty;

        /// <summary>
        /// 1-based upload order — server-assigned at creation time.
        /// Reflects the order in which the user attached the files.
        /// </summary>
        public int DisplayOrder { get; private set; }

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private Attachment() { }

        private Attachment(Guid id,
                           Guid userId,
                           Guid taskId,
                           string fileName,
                           string contentType,
                           long sizeBytes,
                           string blobPath,
                           int displayOrder,
                           DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            TaskId = taskId;
            FileName = fileName;
            ContentType = contentType;
            SizeBytes = sizeBytes;
            BlobPath = blobPath;
            DisplayOrder = displayOrder;
        }

        // FACTORY

        /// <summary>
        /// Creates a new <see cref="Attachment"/> record after a successful blob upload.
        /// </summary>
        /// <param name="id">
        /// Pre-generated attachment ID.
        /// The handler generates this before the upload so the same ID can be
        /// embedded in the blob path.
        /// </param>
        /// <param name="userId">Owner of the attachment (tenant boundary).</param>
        /// <param name="taskId">Parent task. Must be non-empty.</param>
        /// <param name="fileName">Original filename; leading/trailing whitespace is trimmed.</param>
        /// <param name="contentType">
        /// MIME type; normalised to "application/octet-stream" when null or empty.
        /// </param>
        /// <param name="sizeBytes">File size in bytes. Must be positive.</param>
        /// <param name="blobPath">Path within blob storage. Must be non-empty.</param>
        /// <param name="displayOrder">1-based upload order. Must be at least 1.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public static DomainResult<Attachment> Create(Guid id,
                                                      Guid userId,
                                                      Guid taskId,
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
                errors.Add(new DomainError("Attachment.Id.Empty", "Id must be a non-empty GUID."));

            if (userId == Guid.Empty)
                errors.Add(new DomainError("Attachment.UserId.Empty", "UserId must be a non-empty GUID."));

            if (taskId == Guid.Empty)
                errors.Add(new DomainError("Attachment.TaskId.Empty", "TaskId must be a non-empty GUID."));

            if (string.IsNullOrWhiteSpace(normalizedFileName))
                errors.Add(new DomainError("Attachment.FileName.Empty", "FileName is required."));

            if (normalizedFileName.Length > MaxFileNameLength)
                errors.Add(new DomainError("Attachment.FileName.TooLong",
                    $"FileName must be at most {MaxFileNameLength} characters."));

            if (normalizedContentType.Length > MaxContentTypeLength)
                errors.Add(new DomainError("Attachment.ContentType.TooLong",
                    $"ContentType must be at most {MaxContentTypeLength} characters."));

            if (string.IsNullOrWhiteSpace(normalizedBlobPath))
                errors.Add(new DomainError("Attachment.BlobPath.Empty", "BlobPath is required."));

            if (normalizedBlobPath.Length > MaxBlobPathLength)
                errors.Add(new DomainError("Attachment.BlobPath.TooLong",
                    $"BlobPath must be at most {MaxBlobPathLength} characters."));

            if (sizeBytes <= 0)
                errors.Add(new DomainError("Attachment.SizeBytes.Invalid",
                    "SizeBytes must be a positive number."));

            if (displayOrder < 1)
                errors.Add(new DomainError("Attachment.DisplayOrder.Invalid",
                    "DisplayOrder must be at least 1."));

            if (errors.Count > 0)
                return DomainResult<Attachment>.Failure(errors);

            var attachment = new Attachment(id: id,
                                            userId: userId,
                                            taskId: taskId,
                                            fileName: normalizedFileName,
                                            contentType: normalizedContentType,
                                            sizeBytes: sizeBytes,
                                            blobPath: normalizedBlobPath,
                                            displayOrder: displayOrder,
                                            utcNow: utcNow);

            return DomainResult<Attachment>.Success(attachment);
        }

        // BEHAVIOURS

        /// <summary>
        /// Soft-deletes this attachment.
        /// Idempotent: soft-deleting an already-deleted attachment returns success.
        ///
        /// Note: The blob in storage is cleaned up separately by the background orphan-cleanup worker.
        /// </summary>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Success(); // Idempotent

            MarkDeleted(utcNow);

            return DomainResult.Success();
        }
    }
}
