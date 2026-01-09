
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FluentResults;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Storage;
using System;
using System.Collections.Generic;
using System.Text;
using static NotesApp.Application.Abstractions.Storage.IBlobStorageService;

namespace NotesApp.Infrastructure.Storage
{
    /// <summary>
    /// Azure Blob Storage implementation of IBlobStorageService.
    /// 
    /// Uses the Azure.Storage.Blobs SDK with best practices:
    /// - BlobServiceClient is thread-safe and reusable (singleton lifetime)
    /// - Async/await throughout
    /// - Proper cancellation token propagation
    /// - User delegation SAS for download URLs (more secure than account key SAS)
    /// - Result pattern for explicit error handling
    /// 
    /// Configuration is handled via Microsoft.Extensions.Azure DI integration.
    //TODO:
    /// Retry policies should be configured via BlobClientOptions in DI registration.
    /// </summary>
    public sealed class AzureBlobStorageService : IBlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<AzureBlobStorageService> _logger;

        public AzureBlobStorageService(BlobServiceClient blobServiceClient,
                                       ILogger<AzureBlobStorageService> logger)
        {
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        /// <inheritdoc />
        public async Task<Result<StorageUploadResult>> UploadAsync(string containerName,
                                                                   string blobPath,
                                                                   Stream content,
                                                                   string contentType,
                                                                   CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
            ArgumentNullException.ThrowIfNull(content);

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(blobPath);
                var effectiveContentType = contentType ?? StorageConstants.DefaultContentType;

                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = effectiveContentType
                    }
                };

                _logger.LogInformation(
                    "Uploading blob to container '{Container}' path '{Path}'",
                    containerName,
                    blobPath);

                var response = await blobClient.UploadAsync(content, uploadOptions, cancellationToken);

                _logger.LogInformation(
                    "Blob uploaded successfully. ETag: {ETag}",
                    response.Value.ETag);

                var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

                return Result.Ok(new StorageUploadResult(
                    BlobPath: blobPath,
                    ContentType: effectiveContentType,
                    SizeBytes: properties.Value.ContentLength,
                    ETag: response.Value.ETag.ToString()));
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure storage error uploading blob to container '{Container}' path '{Path}'. Status: {Status}",
                    containerName,
                    blobPath,
                    ex.Status);

                return Result.Fail(new Error("Blob.Upload.Failed")
                    .WithMetadata("Status", ex.Status)
                    .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
            }
        }


        /// <inheritdoc />
        public async Task<Result<StorageDownloadResult>> DownloadAsync(string containerName,
                                                                       string blobPath,
                                                                       CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobPath);

                var response = await blobClient.DownloadAsync(cancellationToken);

                return Result.Ok(new StorageDownloadResult(
                    content: response.Value.Content,
                    contentType: response.Value.ContentType,
                    sizeBytes: response.Value.ContentLength));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning(
                    "Blob not found: container '{Container}' path '{Path}'",
                    containerName,
                    blobPath);

                return Result.Fail(new Error("Blob.NotFound")
                    .WithMetadata("Container", containerName)
                    .WithMetadata("Path", blobPath));
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure storage error downloading blob from container '{Container}' path '{Path}'. Status: {Status}",
                    containerName,
                    blobPath,
                    ex.Status);

                return Result.Fail(new Error("Blob.Download.Failed")
                    .WithMetadata("Status", ex.Status)
                    .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
            }
        }


       /// <inheritdoc />
        public async Task<Result> DeleteAsync(string containerName,
                                              string blobPath,
                                              CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobPath);

                var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

                if (response.Value)
                {
                    _logger.LogInformation(
                        "Blob deleted: container '{Container}' path '{Path}'",
                        containerName,
                        blobPath);
                }
                else
                {
                    _logger.LogDebug(
                        "Blob did not exist for deletion: container '{Container}' path '{Path}'",
                        containerName,
                        blobPath);
                }

                return Result.Ok();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure storage error deleting blob from container '{Container}' path '{Path}'. Status: {Status}",
                    containerName,
                    blobPath,
                    ex.Status);

                return Result.Fail(new Error("Blob.Delete.Failed")
                    .WithMetadata("Status", ex.Status)
                    .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool>> ExistsAsync(string containerName,
                                                    string blobPath,
                                                    CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobPath);

                var response = await blobClient.ExistsAsync(cancellationToken);

                return Result.Ok(response.Value);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure storage error checking existence of blob in container '{Container}' path '{Path}'. Status: {Status}",
                    containerName,
                    blobPath,
                    ex.Status);

                return Result.Fail(new Error("Blob.Exists.Failed")
                    .WithMetadata("Status", ex.Status)
                    .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
            }
        }


        /// <inheritdoc />
        public async Task<Result<string>> GenerateDownloadUrlAsync(string containerName,
                                                                   string blobPath,
                                                                   TimeSpan validity,
                                                                   CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            if (validity <= TimeSpan.Zero)
            {
                return Result.Fail(new Error("Blob.Url.InvalidValidity")
                    .WithMetadata("Message", "Validity must be positive."));
            }

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobPath);

                var startsOn = DateTimeOffset.UtcNow;
                var expiresOn = startsOn.Add(validity);

                var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(startsOn,
                                                                                           expiresOn,
                                                                                           cancellationToken);

                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = containerClient.Name,
                    BlobName = blobClient.Name,
                    Resource = "b",
                    StartsOn = startsOn,
                    ExpiresOn = expiresOn
                };

                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
                {
                    Sas = sasBuilder.ToSasQueryParameters(
                        userDelegationKey.Value,
                        _blobServiceClient.AccountName)
                };

                var sasUri = blobUriBuilder.ToUri().ToString();

                _logger.LogDebug(
                    "Generated download URL for blob '{Path}' valid until {ExpiresOn}",
                    blobPath,
                    expiresOn);

                return Result.Ok(sasUri);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure storage error generating download URL for blob in container '{Container}' path '{Path}'. Status: {Status}",
                    containerName,
                    blobPath,
                    ex.Status);

                return Result.Fail(new Error("Blob.Url.GenerationFailed")
                    .WithMetadata("Status", ex.Status)
                    .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
            }
        }
    }
}
