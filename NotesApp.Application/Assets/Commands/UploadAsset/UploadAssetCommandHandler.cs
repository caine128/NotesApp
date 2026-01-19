using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Assets.Models;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using static NotesApp.Application.Abstractions.Storage.IBlobStorageService;

namespace NotesApp.Application.Assets.Commands.UploadAsset
{
    /// <summary>
    /// Handles asset upload requests.
    /// 
    /// Workflow:
    /// 1. [Validator] Input validation (BlockId, AssetClientId, FileName, SizeBytes, Content)
    /// 2. Load block WITHOUT tracking
    /// 3. Validate block state (ownership, type, status, AssetClientId match)
    /// 4. Check idempotency (existing asset)
    /// 5. Upload binary to blob storage ← POINT OF NO RETURN
    /// 6. Create Asset entity (in memory)
    /// 7. Link asset to block (in memory)
    /// 8. Create both outbox messages - FAIL if any error
    /// 9. Persist everything atomically
    /// 10. Return download URL (best effort - may be null on transient failure)
    /// 
    /// If blob upload fails, the block is marked as UploadFailed.
    /// If any step after blob upload fails, the blob is cleaned up (best effort).
    /// </summary>
    public sealed class UploadAssetCommandHandler
        : IRequestHandler<UploadAssetCommand, Result<UploadAssetResultDto>>
    {
        private readonly IBlockRepository _blockRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly AssetStorageOptions _options;
        private readonly ILogger<UploadAssetCommandHandler> _logger;

        public UploadAssetCommandHandler(IBlockRepository blockRepository,
                                         IAssetRepository assetRepository,
                                         IBlobStorageService blobStorageService,
                                         ICurrentUserService currentUserService,
                                         IOutboxRepository outboxRepository,
                                         IUnitOfWork unitOfWork,
                                         ISystemClock clock,
                                         IOptions<AssetStorageOptions> options,
                                         ILogger<UploadAssetCommandHandler> logger)
        {
            _blockRepository = blockRepository;
            _assetRepository = assetRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _options = options.Value;
            _logger = logger;
        }


        public async Task<Result<UploadAssetResultDto>> Handle(UploadAssetCommand request,
                                                               CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation(
                "Asset upload requested for block {BlockId} by user {UserId}",
                request.BlockId,
                userId);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 1: Business Validation (requires database access)
            // 
            // Note: Input validation (BlockId, AssetClientId, FileName, ContentType,
            // SizeBytes, Content) is handled by UploadAssetCommandValidator.
            // ═══════════════════════════════════════════════════════════════════

           
            // Load block WITHOUT tracking
            var block = await _blockRepository.GetByIdUntrackedAsync(request.BlockId, cancellationToken);

            if (block is null || block.UserId != userId)
            {
                return Result.Fail(new Error("Block.NotFound")
                    .WithMetadata("Message", "Block not found."));
            }

            if (!Block.IsAssetBlockType(block.Type))
            {
                return Result.Fail(new Error("Block.Type.Invalid")
                    .WithMetadata("Message", "Block is not an asset block type."));
            }

            if (block.UploadStatus != UploadStatus.Pending)
            {
                return Result.Fail(new Error("Block.Upload.InvalidStatus")
                    .WithMetadata("Message", $"Block upload status is {block.UploadStatus}, expected Pending."));
            }

            if (block.AssetClientId != request.AssetClientId)
            {
                return Result.Fail(new Error("Asset.ClientId.Mismatch")
                    .WithMetadata("Message", "AssetClientId does not match block's expected value."));
            }

            // Check idempotency
            var existingAsset = await _assetRepository.GetByBlockIdAsync(request.BlockId, cancellationToken);
            if (existingAsset is not null)
            {
                _logger.LogWarning("Asset already exists for block {BlockId}. Returning existing asset {AssetId}.",
                                   request.BlockId,
                                   existingAsset.Id);

                var existingUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(_options.ContainerName,
                                                                                           existingAsset.BlobPath,
                                                                                           _options.DownloadUrlValidity,
                                                                                           cancellationToken);

                if (existingUrlResult.IsFailed)
                {
                    return Result.Fail(existingUrlResult.Errors);
                }

                return Result.Ok(new UploadAssetResultDto
                {
                    AssetId = existingAsset.Id,
                    BlockId = existingAsset.BlockId,
                    DownloadUrl = existingUrlResult.Value
                });
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 2: Blob Upload (POINT OF NO RETURN)
            // ═══════════════════════════════════════════════════════════════════

            var blobPath = GenerateBlobPath(userId, block.ParentId, block.Id, request.FileName);

            var uploadResult = await _blobStorageService.UploadAsync(_options.ContainerName,
                                                                     blobPath,
                                                                     request.Content,
                                                                     request.ContentType,
                                                                     cancellationToken);

            if (uploadResult.IsFailed)
            {
                _logger.LogError("Failed to upload asset to blob storage for block {BlockId}: {Errors}",
                                 request.BlockId,
                                 string.Join(", ", uploadResult.Errors.Select(e => e.Message)));

                // Mark block upload as failed and persist
                block.SetUploadFailed(utcNow);
                _blockRepository.Update(block);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Fail(new Error("Asset.Upload.Failed")
                    .WithMetadata("Message", "Failed to upload asset to storage."));
            }

            _logger.LogInformation("Asset uploaded to blob storage: {BlobPath}, Size: {SizeBytes}",
                                   blobPath,
                                   uploadResult.Value.SizeBytes);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 3: Create Entities and Outbox (in memory, validate all)
            // ═══════════════════════════════════════════════════════════════════

            // Create Asset entity
            var assetResult = Asset.Create(userId,
                                           block.Id,
                                           request.FileName,
                                           request.ContentType,
                                           uploadResult.Value.SizeBytes,
                                           blobPath,
                                           utcNow);

            if (assetResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to create Asset entity: {Errors}",
                    string.Join(", ", assetResult.Errors.Select(e => e.Message)));

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Asset.Create.Failed")
                    .WithMetadata("Message", "Failed to create asset record."));
            }

            var asset = assetResult.Value!;

            // Link asset to block (in memory - block is untracked)
            var linkResult = block.SetAssetUploaded(asset.Id, utcNow);
            if (linkResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to link asset to block: {Errors}",
                    string.Join(", ", linkResult.Errors.Select(e => e.Message)));

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Block.LinkAsset.Failed")
                    .WithMetadata("Message", "Failed to link asset to block."));
            }

            // Create Asset outbox message - MUST succeed
            var assetPayload = OutboxPayloadBuilder.BuildAssetPayload(asset, Guid.Empty);
            var assetOutboxResult = OutboxMessage.Create<Asset, AssetEventType>(
                asset,
                AssetEventType.Created,
                assetPayload,
                utcNow);

            if (assetOutboxResult.IsFailure || assetOutboxResult.Value is null)
            {
                _logger.LogError(
                    "Failed to create Asset outbox message for block {BlockId}",
                    request.BlockId);

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Outbox.Asset.CreateFailed")
                    .WithMetadata("Message", "Failed to create asset sync event."));
            }

            // Create Block outbox message - MUST succeed
            var blockPayload = OutboxPayloadBuilder.BuildBlockPayload(block, Guid.Empty);
            var blockOutboxResult = OutboxMessage.Create<Block, BlockEventType>(
                block,
                BlockEventType.Updated,
                blockPayload,
                utcNow);

            if (blockOutboxResult.IsFailure || blockOutboxResult.Value is null)
            {
                _logger.LogError(
                    "Failed to create Block outbox message for block {BlockId}",
                    request.BlockId);

                await CleanupBlobAsync(blobPath, cancellationToken);

                return Result.Fail(new Error("Outbox.Block.CreateFailed")
                    .WithMetadata("Message", "Failed to create block sync event."));
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 4: Persist Everything (atomic)
            // ═══════════════════════════════════════════════════════════════════

            await _assetRepository.AddAsync(asset, cancellationToken);
            _blockRepository.Update(block);
            await _outboxRepository.AddAsync(assetOutboxResult.Value, cancellationToken);
            await _outboxRepository.AddAsync(blockOutboxResult.Value, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 5: Generate Download URL
            // ═══════════════════════════════════════════════════════════════════

            // URL generation is a read-only operation that can fail transiently.
            // Since the asset is already successfully created and persisted,
            // we should not fail the entire operation. Instead:
            // - Try to generate URL
            // - If it fails, return success with null URL
            // - Client can fetch the URL later via a separate endpoint or retry

            string? downloadUrl = null;

            var downloadUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(_options.ContainerName,
                                                                                       blobPath,
                                                                                       _options.DownloadUrlValidity,
                                                                                       cancellationToken);

            if (downloadUrlResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to generate download URL for asset {AssetId}. " +
                    "Asset was created successfully - client can fetch URL separately. Errors: {Errors}",
                    asset.Id,
                    string.Join(", ", downloadUrlResult.Errors.Select(e => e.Message)));

                // Don't fail - asset is created, URL can be fetched later
            }
            else
            {
                downloadUrl = downloadUrlResult.Value;
            }

            _logger.LogInformation("Asset {AssetId} created and linked to block {BlockId}",
                                   asset.Id,
                                   block.Id);

            return Result.Ok(new UploadAssetResultDto
            {
                AssetId = asset.Id,
                BlockId = block.Id,
                DownloadUrl = downloadUrl  // May be null if URL generation failed
            });
        }

        /// <summary>
        /// Attempts to clean up an uploaded blob. Best effort - does not throw on failure.
        /// </summary>
        private async Task CleanupBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            var deleteResult = await _blobStorageService.DeleteAsync(_options.ContainerName, blobPath, cancellationToken);
            if (deleteResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to clean up blob after failure: {BlobPath}. Errors: {Errors}",
                    blobPath,
                    string.Join(", ", deleteResult.Errors.Select(e => e.Message)));
            }
        }

        /// <summary>
        /// Generates a hierarchical blob path for the asset.
        /// Format: {userId}/{parentId}/{blockId}/{sanitizedFilename}
        /// </summary>
        private static string GenerateBlobPath(Guid userId, Guid parentId, Guid blockId, string fileName)
        {
            // Sanitize filename to remove path characters
            var sanitizedFileName = SanitizeFileName(fileName);

            return $"{userId}/{parentId}/{blockId}/{sanitizedFileName}";
        }

        /// <summary>
        /// Removes path separators and other problematic characters from filename.
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "file";
            }

            // Remove path separators and problematic characters
            var invalid = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            var sanitized = fileName;

            foreach (var c in invalid)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Ensure it's not empty after sanitization
            return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized.Trim();
        }
    }
}
