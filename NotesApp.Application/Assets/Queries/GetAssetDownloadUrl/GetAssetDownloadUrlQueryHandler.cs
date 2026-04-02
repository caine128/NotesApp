using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;

namespace NotesApp.Application.Assets.Queries.GetAssetDownloadUrl
{
    /// <summary>
    /// Handles requests to generate a pre-signed download URL for a specific asset.
    ///
    /// Workflow:
    /// 1. Resolve current user
    /// 2. Load asset by ID — return Asset.NotFound if missing or not owned by user
    /// 3. Call blob storage to generate a pre-signed URL
    /// 4. Return the URL (or propagate the storage failure as a Result error)
    ///
    /// This query is intentionally separate from the sync pull response so that
    /// transient blob storage failures never affect sync correctness.
    /// </summary>
    public sealed class GetAssetDownloadUrlQueryHandler
        : IRequestHandler<GetAssetDownloadUrlQuery, Result<string>>
    {
        private readonly IAssetRepository _assetRepository;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AssetStorageOptions _assetOptions;
        private readonly ILogger<GetAssetDownloadUrlQueryHandler> _logger;

        public GetAssetDownloadUrlQueryHandler(IAssetRepository assetRepository,
                                               IBlobStorageService blobStorageService,
                                               ICurrentUserService currentUserService,
                                               IOptions<AssetStorageOptions> assetOptions,
                                               ILogger<GetAssetDownloadUrlQueryHandler> logger)
        {
            _assetRepository = assetRepository;
            _blobStorageService = blobStorageService;
            _currentUserService = currentUserService;
            _assetOptions = assetOptions.Value;
            _logger = logger;
        }

        public async Task<Result<string>> Handle(GetAssetDownloadUrlQuery request,
                                                  CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var asset = await _assetRepository.GetByIdAsync(request.AssetId, cancellationToken);

            if (asset is null || asset.UserId != userId || asset.IsDeleted)
            {
                return Result.Fail(new Error("Asset.NotFound")
                    .WithMetadata("Message", "Asset not found."));
            }

            _logger.LogInformation("Generating download URL for asset {AssetId} requested by user {UserId}",
                                   asset.Id,
                                   userId);

            var urlResult = await _blobStorageService.GenerateDownloadUrlAsync(_assetOptions.ContainerName,
                                                                               asset.BlobPath,
                                                                               _assetOptions.DownloadUrlValidity,
                                                                               cancellationToken);

            if (urlResult.IsFailed)
            {
                _logger.LogError("Failed to generate download URL for asset {AssetId}: {Errors}",
                                 asset.Id,
                                 string.Join(", ", urlResult.Errors.Select(e => e.Message)));

                return Result.Fail(new Error("Asset.DownloadUrl.GenerationFailed")
                    .WithMetadata("Message", "Failed to generate asset download URL."));
            }

            return Result.Ok(urlResult.Value);
        }
    }
}
