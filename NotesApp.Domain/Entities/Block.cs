using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A content block within a Note or Task.
    /// Blocks are ordered components that contain either text (Markdown) or asset references.
    /// 
    /// Invariants:
    /// - UserId must be non-empty.
    /// - ParentId must be non-empty.
    /// - Position must be non-empty (fractional index for ordering).
    /// - Text blocks must have TextContent (can be empty string).
    /// - Asset blocks must have AssetClientId, AssetFileName, and valid AssetSizeBytes.
    /// 
    /// Note: Block does NOT implement ICalendarEntity as it is a component
    /// of calendar entities (Note/Task), not a calendar entity itself.
    /// </summary>
    public sealed class Block : Entity<Guid>
    {
        private const int MaxPositionLength = 100;
        private const int MaxAssetClientIdLength = 100;
        private const int MaxAssetFileNameLength = 256;
        private const int MaxAssetContentTypeLength = 100;

        /// <summary>
        /// Owner of this block (tenant boundary, must match parent's UserId).
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// The Note or Task this block belongs to.
        /// </summary>
        public Guid ParentId { get; private set; }

        /// <summary>
        /// Type of parent entity (Note or Task).
        /// </summary>
        public BlockParentType ParentType { get; private set; }

        /// <summary>
        /// Type of this block (Paragraph, Image, etc.).
        /// </summary>
        public BlockType Type { get; private set; }

        /// <summary>
        /// Fractional index for ordering blocks within parent.
        /// Lexicographically sortable string (e.g., "a0", "a1", "a0V").
        /// </summary>
        public string Position { get; private set; } = string.Empty;

        /// <summary>
        /// Markdown text content for text-based blocks.
        /// Null for asset blocks.
        /// </summary>
        public string? TextContent { get; private set; }

        /// <summary>
        /// Reference to the uploaded Asset entity. Null until upload completes.
        /// </summary>
        public Guid? AssetId { get; private set; }

        /// <summary>
        /// Client-generated identifier for tracking upload across sync.
        /// </summary>
        public string? AssetClientId { get; private set; }

        /// <summary>
        /// Original filename from client.
        /// </summary>
        public string? AssetFileName { get; private set; }

        /// <summary>
        /// MIME type (e.g., "image/jpeg").
        /// </summary>
        public string? AssetContentType { get; private set; }

        /// <summary>
        /// File size in bytes (for quota/validation).
        /// </summary>
        public long? AssetSizeBytes { get; private set; }

        /// <summary>
        /// Current upload status for asset blocks.
        /// </summary>
        public UploadStatus UploadStatus { get; private set; }

        /// <summary>
        /// Monotonic business version used for sync/conflict detection.
        /// Starts at 1 and increments on every meaningful mutation.
        /// </summary>
        public long Version { get; private set; } = 1;

        // EF Core constructor
        private Block()
        {
        }

        private Block(Guid id,
                      Guid userId,
                      Guid parentId,
                      BlockParentType parentType,
                      BlockType type,
                      string position,
                      string? textContent,
                      string? assetClientId,
                      string? assetFileName,
                      string? assetContentType,
                      long? assetSizeBytes,
                      UploadStatus uploadStatus,
                      DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            ParentId = parentId;
            ParentType = parentType;
            Type = type;
            Position = position;
            TextContent = textContent;
            AssetClientId = assetClientId;
            AssetFileName = assetFileName;
            AssetContentType = assetContentType;
            AssetSizeBytes = assetSizeBytes;
            UploadStatus = uploadStatus;
            Version = 1;
        }

        // ─────────────────────────────────────────────────────────────────
        // Factory Methods
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a text-based block (Paragraph, Heading, Quote, etc.).
        /// </summary>
        public static DomainResult<Block> CreateTextBlock(Guid userId,
                                                          Guid parentId,
                                                          BlockParentType parentType,
                                                          BlockType type,
                                                          string position,
                                                          string? textContent,
                                                          DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedPosition = position?.Trim() ?? string.Empty;
            var normalizedTextContent = textContent; // Don't trim - preserve whitespace in content

            if (userId == Guid.Empty)
                errors.Add(new DomainError("Block.UserId.Empty", "UserId must be a non-empty GUID."));

            if (parentId == Guid.Empty)
                errors.Add(new DomainError("Block.ParentId.Empty", "ParentId must be a non-empty GUID."));

            if (string.IsNullOrWhiteSpace(normalizedPosition))
                errors.Add(new DomainError("Block.Position.Empty", "Position is required."));

            if (normalizedPosition.Length > MaxPositionLength)
                errors.Add(new DomainError("Block.Position.TooLong", $"Position must be at most {MaxPositionLength} characters."));

            if (!IsTextBlockType(type))
                errors.Add(new DomainError("Block.Type.Invalid", $"Type '{type}' is not a text block type."));

            if (errors.Count > 0)
                return DomainResult<Block>.Failure(errors);

            var block = new Block(id: Guid.NewGuid(),
                                  userId: userId,
                                  parentId: parentId,
                                  parentType: parentType,
                                  type: type,
                                  position: normalizedPosition,
                                  textContent: normalizedTextContent,
                                  assetClientId: null,
                                  assetFileName: null,
                                  assetContentType: null,
                                  assetSizeBytes: null,
                                  uploadStatus: UploadStatus.NotApplicable,
                                  utcNow: utcNow);

            return DomainResult<Block>.Success(block);
        }


        /// <summary>
        /// Creates an asset-based block (Image, File).
        /// Asset metadata is captured; actual upload happens separately.
        /// </summary>
        public static DomainResult<Block> CreateAssetBlock(Guid userId,
                                                           Guid parentId,
                                                           BlockParentType parentType,
                                                           BlockType type,
                                                           string position,
                                                           string assetClientId,
                                                           string assetFileName,
                                                           string? assetContentType,
                                                           long assetSizeBytes,
                                                           DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedPosition = position?.Trim() ?? string.Empty;
            var normalizedAssetClientId = assetClientId?.Trim() ?? string.Empty;
            var normalizedAssetFileName = assetFileName?.Trim() ?? string.Empty;
            var normalizedAssetContentType = string.IsNullOrWhiteSpace(assetContentType)
                ? "application/octet-stream"
                : assetContentType.Trim();

            if (userId == Guid.Empty)
                errors.Add(new DomainError("Block.UserId.Empty", "UserId must be a non-empty GUID."));

            if (parentId == Guid.Empty)
                errors.Add(new DomainError("Block.ParentId.Empty", "ParentId must be a non-empty GUID."));

            if (string.IsNullOrWhiteSpace(normalizedPosition))
                errors.Add(new DomainError("Block.Position.Empty", "Position is required."));

            if (normalizedPosition.Length > MaxPositionLength)
                errors.Add(new DomainError("Block.Position.TooLong", $"Position must be at most {MaxPositionLength} characters."));

            if (!IsAssetBlockType(type))
                errors.Add(new DomainError("Block.Type.Invalid", $"Type '{type}' is not an asset block type."));

            if (string.IsNullOrWhiteSpace(normalizedAssetClientId))
                errors.Add(new DomainError("Block.AssetClientId.Empty", "AssetClientId is required for asset blocks."));

            if (normalizedAssetClientId.Length > MaxAssetClientIdLength)
                errors.Add(new DomainError("Block.AssetClientId.TooLong", $"AssetClientId must be at most {MaxAssetClientIdLength} characters."));

            if (string.IsNullOrWhiteSpace(normalizedAssetFileName))
                errors.Add(new DomainError("Block.AssetFileName.Empty", "AssetFileName is required for asset blocks."));

            if (normalizedAssetFileName.Length > MaxAssetFileNameLength)
                errors.Add(new DomainError("Block.AssetFileName.TooLong", $"AssetFileName must be at most {MaxAssetFileNameLength} characters."));

            if (normalizedAssetContentType.Length > MaxAssetContentTypeLength)
                errors.Add(new DomainError("Block.AssetContentType.TooLong", $"AssetContentType must be at most {MaxAssetContentTypeLength} characters."));

            if (assetSizeBytes <= 0)
                errors.Add(new DomainError("Block.AssetSizeBytes.Invalid", "AssetSizeBytes must be a positive number."));

            if (errors.Count > 0)
                return DomainResult<Block>.Failure(errors);

            var block = new Block(id: Guid.NewGuid(),
                                  userId: userId,
                                  parentId: parentId,
                                  parentType: parentType,
                                  type: type,
                                  position: normalizedPosition,
                                  textContent: null,
                                  assetClientId: normalizedAssetClientId,
                                  assetFileName: normalizedAssetFileName,
                                  assetContentType: normalizedAssetContentType,
                                  assetSizeBytes: assetSizeBytes,
                                  uploadStatus: UploadStatus.Pending,
                                  utcNow: utcNow);

            return DomainResult<Block>.Success(block);
        }


        // ─────────────────────────────────────────────────────────────────
        // Behaviors
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the text content of a text block.
        /// </summary>
        public DomainResult UpdateTextContent(string? textContent, DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Failure(new DomainError("Block.Deleted", "Cannot update a deleted block."));

            if (!IsTextBlockType(Type))
                return DomainResult.Failure(new DomainError("Block.Type.Invalid", "Cannot set text content on an asset block."));

            TextContent = textContent;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Updates the position (fractional index) of this block.
        /// </summary>
        public DomainResult UpdatePosition(string position, DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Failure(new DomainError("Block.Deleted", "Cannot update a deleted block."));

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedPosition))
                return DomainResult.Failure(new DomainError("Block.Position.Empty", "Position is required."));

            if (normalizedPosition.Length > MaxPositionLength)
                return DomainResult.Failure(new DomainError("Block.Position.TooLong", $"Position must be at most {MaxPositionLength} characters."));

            Position = normalizedPosition;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Links the uploaded asset to this block.
        /// Called after successful upload to blob storage.
        /// </summary>
        public DomainResult SetAssetUploaded(Guid assetId, DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Failure(new DomainError("Block.Deleted", "Cannot update a deleted block."));

            if (!IsAssetBlockType(Type))
                return DomainResult.Failure(new DomainError("Block.Type.Invalid", "Cannot set asset on a text block."));

            if (assetId == Guid.Empty)
                return DomainResult.Failure(new DomainError("Block.AssetId.Empty", "AssetId must be a non-empty GUID."));

            if (AssetId.HasValue)
                return DomainResult.Failure(new DomainError("Block.Asset.AlreadySet", "Block already has an asset linked."));

            AssetId = assetId;
            UploadStatus = UploadStatus.Synced;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Marks the asset upload as failed.
        /// </summary>
        public DomainResult SetUploadFailed(DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Failure(new DomainError("Block.Deleted", "Cannot update a deleted block."));

            if (!IsAssetBlockType(Type))
                return DomainResult.Failure(new DomainError("Block.Type.Invalid", "Cannot set upload status on a text block."));

            UploadStatus = UploadStatus.Failed;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Soft-deletes this block.
        /// </summary>
        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
                return DomainResult.Success(); // Idempotent

            IncrementVersion();
            MarkDeleted(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Restores a previously deleted block.
        /// </summary>
        public DomainResult RestoreBlock(DateTime utcNow)
        {
            if (!IsDeleted)
                return DomainResult.Success(); // Idempotent

            IncrementVersion();
            Restore(utcNow);

            return DomainResult.Success();
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private void IncrementVersion()
        {
            Version++;
        }

        /// <summary>
        /// Checks if the block type is a text-based type.
        /// </summary>
        public static bool IsTextBlockType(BlockType type)
        {
            return type switch
            {
                BlockType.Paragraph => true,
                BlockType.Heading1 => true,
                BlockType.Heading2 => true,
                BlockType.Heading3 => true,
                BlockType.BulletList => true,
                BlockType.NumberedList => true,
                BlockType.Quote => true,
                BlockType.Code => true,
                BlockType.Divider => true,
                _ => false
            };
        }

        /// <summary>
        /// Checks if the block type is an asset-based type.
        /// </summary>
        public static bool IsAssetBlockType(BlockType type)
        {
            return type switch
            {
                BlockType.Image => true,
                BlockType.File => true,
                _ => false
            };
        }
    }
}
