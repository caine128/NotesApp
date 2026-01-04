using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Represents an uploaded file in blob storage.
    /// 
    /// Immutable after creation: no updates allowed, only create or delete.
    /// This simplifies sync (no version tracking needed) and blob management.
    /// 
    /// Invariants:
    /// - UserId must be non-empty.
    /// - BlockId must be non-empty.
    /// - FileName must be non-empty.
    /// - BlobPath must be non-empty.
    /// - SizeBytes must be positive.
    /// </summary>
    public sealed class Asset : Entity<Guid>
    {
        private const int MaxFileNameLength = 256;
        private const int MaxContentTypeLength = 100;
        private const int MaxBlobPathLength = 500;

        /// <summary>
        /// Owner of this asset (tenant boundary, must match block's UserId).
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// The Block this asset belongs to (1:1 relationship).
        /// </summary>
        public Guid BlockId { get; private set; }

        /// <summary>
        /// Original filename from client.
        /// </summary>
        public string FileName { get; private set; } = string.Empty;

        /// <summary>
        /// MIME type (e.g., "image/jpeg").
        /// </summary>
        public string ContentType { get; private set; } = string.Empty;

        /// <summary>
        /// File size in bytes.
        /// </summary>
        public long SizeBytes { get; private set; }

        /// <summary>
        /// Path within blob storage.
        /// Format: {userId}/{parentId}/{blockId}/{filename}
        /// </summary>
        public string BlobPath { get; private set; } = string.Empty;

        // EF Core constructor
        private Asset()
        {
        }

        private Asset(Guid id,
                      Guid userId,
                      Guid blockId,
                      string fileName,
                      string contentType,
                      long sizeBytes,
                      string blobPath,
                      DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            BlockId = blockId;
            FileName = fileName;
            ContentType = contentType;
            SizeBytes = sizeBytes;
            BlobPath = blobPath;
        }

        // ─────────────────────────────────────────────────────────────────
        // Factory Method
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new Asset record after successful upload to blob storage.
        /// </summary>
        public static DomainResult<Asset> Create(Guid userId,
                                                 Guid blockId,
                                                 string fileName,
                                                 string? contentType,
                                                 long sizeBytes,
                                                 string blobPath,
                                                 DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedFileName = fileName?.Trim() ?? string.Empty;
            var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim();
            var normalizedBlobPath = blobPath?.Trim() ?? string.Empty;

            if (userId == Guid.Empty)
                errors.Add(new DomainError("Asset.UserId.Empty", "UserId must be a non-empty GUID."));

            if (blockId == Guid.Empty)
                errors.Add(new DomainError("Asset.BlockId.Empty", "BlockId must be a non-empty GUID."));

            if (string.IsNullOrWhiteSpace(normalizedFileName))
                errors.Add(new DomainError("Asset.FileName.Empty", "FileName is required."));

            if (normalizedFileName.Length > MaxFileNameLength)
                errors.Add(new DomainError("Asset.FileName.TooLong", $"FileName must be at most {MaxFileNameLength} characters."));

            if (normalizedContentType.Length > MaxContentTypeLength)
                errors.Add(new DomainError("Asset.ContentType.TooLong", $"ContentType must be at most {MaxContentTypeLength} characters."));

            if (string.IsNullOrWhiteSpace(normalizedBlobPath))
                errors.Add(new DomainError("Asset.BlobPath.Empty", "BlobPath is required."));

            if (normalizedBlobPath.Length > MaxBlobPathLength)
                errors.Add(new DomainError("Asset.BlobPath.TooLong", $"BlobPath must be at most {MaxBlobPathLength} characters."));

            if (sizeBytes <= 0)
                errors.Add(new DomainError("Asset.SizeBytes.Invalid", "SizeBytes must be a positive number."));

            if (errors.Count > 0)
                return DomainResult<Asset>.Failure(errors);

            var asset = new Asset(id: Guid.NewGuid(),
                                  userId: userId,
                                  blockId: blockId,
                                  fileName: normalizedFileName,
                                  contentType: normalizedContentType,
                                  sizeBytes: sizeBytes,
                                  blobPath: normalizedBlobPath,
                                  utcNow: utcNow);

            return DomainResult<Asset>.Success(asset);
        }

        // ─────────────────────────────────────────────────────────────────
        // Behaviors
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Soft-deletes this asset.
        /// Note: The blob in storage should be cleaned up separately by a background job.
        /// </summary>
        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Success(); // Idempotent

            MarkDeleted(utcNow);

            return DomainResult.Success();
        }
    }
}
