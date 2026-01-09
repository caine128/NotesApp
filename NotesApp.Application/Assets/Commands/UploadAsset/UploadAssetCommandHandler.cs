using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
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
    /// 1. Validate block exists and belongs to current user
    /// 2. Verify block is awaiting upload (UploadStatus.Pending)
    /// 3. Verify AssetClientId matches
    /// 4. Upload binary to blob storage
    /// 5. Create Asset entity
    /// 6. Link asset to block
    /// 7. Return download URL
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
        private readonly ILogger<UploadAssetCommandHandler> _logger;

        //TODO: Make this configurable and move to settings
        /// <summary>
        /// Container name for user assets in blob storage.
        /// </summary>
        private const string AssetContainerName = "user-assets";

        //TODO: Make this configurable and move to settings
        /// <summary>
        /// Maximum allowed file size (50 MB).
        /// </summary>
        private const long MaxFileSizeBytes = 50 * 1024 * 1024;

        //TODO: Make this configurable and move to settings
        /// <summary>
        /// Validity period for download URLs returned after upload.
        /// </summary>
        private static readonly TimeSpan DownloadUrlValidity = TimeSpan.FromHours(1);

        public UploadAssetCommandHandler(IBlockRepository blockRepository,
                                         IAssetRepository assetRepository,
                                         IBlobStorageService blobStorageService,
                                         ICurrentUserService currentUserService,
                                         IOutboxRepository outboxRepository,
                                         IUnitOfWork unitOfWork,
                                         ISystemClock clock,
                                         ILogger<UploadAssetCommandHandler> logger)
        {
            _blockRepository = blockRepository;
            _assetRepository = assetRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
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

            // Validate file size
            if (request.SizeBytes <= 0)
            {
                return Result.Fail(new Error("Asset.Size.Invalid")
                    .WithMetadata("Message", "File size must be positive."));
            }

            if (request.SizeBytes > MaxFileSizeBytes)
            {
                return Result.Fail(new Error("Asset.Size.TooLarge")
                    .WithMetadata("Message", $"File size exceeds maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB."));
            }

            // Find the block
            var block = await _blockRepository.GetByIdAsync(request.BlockId, cancellationToken);

            if (block is null)
            {
                return Result.Fail(new Error("Block.NotFound")
                    .WithMetadata("Message", "Block not found."));
            }

            // Verify ownership
            if (block.UserId != userId)
            {
                return Result.Fail(new Error("Block.NotFound")
                    .WithMetadata("Message", "Block not found."));
            }

            // Verify block is an asset block
            if (!Block.IsAssetBlockType(block.Type))
            {
                return Result.Fail(new Error("Block.Type.Invalid")
                    .WithMetadata("Message", "Block is not an asset block type."));
            }

            // Verify block is awaiting upload
            if (block.UploadStatus != UploadStatus.Pending)
            {
                return Result.Fail(new Error("Block.Upload.InvalidStatus")
                    .WithMetadata("Message", $"Block upload status is {block.UploadStatus}, expected Pending."));
            }

            // Verify AssetClientId matches
            if (block.AssetClientId != request.AssetClientId)
            {
                return Result.Fail(new Error("Asset.ClientId.Mismatch")
                    .WithMetadata("Message", "AssetClientId does not match block's expected value."));
            }

            // Check if asset already exists for this block (idempotency)
            var existingAsset = await _assetRepository.GetByBlockIdAsync(request.BlockId, cancellationToken);
            if (existingAsset is not null)
            {
                _logger.LogWarning(
                    "Asset already exists for block {BlockId}. Returning existing asset {AssetId}.",
                    request.BlockId,
                    existingAsset.Id);

                // Generate download URL for existing asset
                var existingUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(
                    AssetContainerName,
                    existingAsset.BlobPath,
                    DownloadUrlValidity,
                    cancellationToken);

                if (existingUrlResult.IsFailed)
                {
                    _logger.LogWarning(
                        "Failed to generate download URL for existing asset {AssetId}: {Errors}",
                        existingAsset.Id,
                        string.Join(", ", existingUrlResult.Errors.Select(e => e.Message)));

                    return Result.Fail(existingUrlResult.Errors);
                }

                return Result.Ok(new UploadAssetResultDto
                {
                    AssetId = existingAsset.Id,
                    BlockId = existingAsset.BlockId,
                    DownloadUrl = existingUrlResult.Value
                });
            }

            // Generate blob path: {userId}/{parentId}/{blockId}/{filename}
            var blobPath = GenerateBlobPath(userId, block.ParentId, block.Id, request.FileName);

            // Upload to blob storage
            var uploadResult = await _blobStorageService.UploadAsync(AssetContainerName,
                                                                     blobPath,
                                                                     request.Content,
                                                                     request.ContentType,
                                                                     cancellationToken);

            if (uploadResult.IsFailed)
            {
                _logger.LogError(
                    "Failed to upload asset to blob storage for block {BlockId}: {Errors}",
                    request.BlockId,
                    string.Join(", ", uploadResult.Errors.Select(e => e.Message)));

                // Mark block upload as failed
                block.SetUploadFailed(utcNow);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Fail(new Error("Asset.Upload.Failed")
                    .WithMetadata("Message", "Failed to upload asset to storage."));
            }

            _logger.LogInformation(
                "Asset uploaded to blob storage: {BlobPath}, Size: {SizeBytes}",
                blobPath,
                uploadResult.Value.SizeBytes);

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

                // Try to clean up the uploaded blob (best effort, don't fail if cleanup fails)
                var deleteResult = await _blobStorageService.DeleteAsync(AssetContainerName, blobPath, cancellationToken);
                if (deleteResult.IsFailed)
                {
                    _logger.LogWarning(
                        "Failed to clean up blob after asset creation failure: {Errors}",
                        string.Join(", ", deleteResult.Errors.Select(e => e.Message)));
                }

                return Result.Fail(new Error("Asset.Create.Failed")
                    .WithMetadata("Message", "Failed to create asset record."));
            }

            var asset = assetResult.Value!;
            await _assetRepository.AddAsync(asset, cancellationToken);

            // Link asset to block
            var linkResult = block.SetAssetUploaded(asset.Id, utcNow);
            if (linkResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to link asset to block: {Errors}",
                    string.Join(", ", linkResult.Errors.Select(e => e.Message)));

                return Result.Fail(new Error("Block.LinkAsset.Failed")
                    .WithMetadata("Message", "Failed to link asset to block."));
            }

            // Create outbox message for the asset creation event
            var assetPayload = OutboxPayloadBuilder.BuildAssetPayload(asset, Guid.Empty);
            var outboxResult = OutboxMessage.Create<Asset, AssetEventType>(
                asset,
                AssetEventType.Created,
                assetPayload,
                utcNow);

            if (outboxResult.IsSuccess)
            {
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            }

            // Create outbox message for the block update event
            var blockPayload = OutboxPayloadBuilder.BuildBlockPayload(block, Guid.Empty);
            var blockOutboxResult = OutboxMessage.Create<Block, BlockEventType>(
                block,
                BlockEventType.Updated,
                blockPayload,
                utcNow);

            if (blockOutboxResult.IsSuccess)
            {
                await _outboxRepository.AddAsync(blockOutboxResult.Value, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Generate download URL
            var downloadUrlResult = await _blobStorageService.GenerateDownloadUrlAsync(AssetContainerName,
                                                                                       blobPath,
                                                                                       DownloadUrlValidity,
                                                                                       cancellationToken);

            if (downloadUrlResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to generate download URL for asset {AssetId}: {Errors}",
                    asset.Id,
                    string.Join(", ", downloadUrlResult.Errors.Select(e => e.Message)));

                return Result.Fail(downloadUrlResult.Errors);
            }

            _logger.LogInformation("Asset {AssetId} created and linked to block {BlockId}",
                                   asset.Id,
                                   block.Id);

            return Result.Ok(new UploadAssetResultDto
            {
                AssetId = asset.Id,
                BlockId = block.Id,
                DownloadUrl = downloadUrlResult.Value
            });
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
