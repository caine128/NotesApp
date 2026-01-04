
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
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
    /// 
    /// Configuration is handled via Microsoft.Extensions.Azure DI integration.
    /// 
    /// References:
    /// - https://learn.microsoft.com/en-us/azure/storage/blobs/storage-quickstart-blobs-dotnet
    /// - https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet
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
        public async Task<BlobUploadResult> UploadAsync(string containerName,
                                                        string blobPath,
                                                        Stream content,
                                                        string contentType,
                                                        CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
            ArgumentNullException.ThrowIfNull(content);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            // Ensure container exists (creates if not)
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(blobPath);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType ?? "application/octet-stream"
                }
            };

            _logger.LogInformation("Uploading blob to container '{Container}' path '{Path}'",
                                   containerName,
                                   blobPath);

            var response = await blobClient.UploadAsync(content, uploadOptions, cancellationToken);

            _logger.LogInformation("Blob uploaded successfully. ETag: {ETag}",
                                   response.Value.ETag);

            // Get the blob properties to return size
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return new BlobUploadResult(blobPath: blobPath,
                                        contentType: contentType ?? "application/octet-stream",
                                        sizeBytes: properties.Value.ContentLength,
                                        eTag: response.Value.ETag.ToString());
        }


        /// <inheritdoc />
        public async Task<BlobDownloadResult?> DownloadAsync(string containerName,
                                                             string blobPath,
                                                             CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            try
            {
                var response = await blobClient.DownloadAsync(cancellationToken);

                return new BlobDownloadResult(content: response.Value.Content,
                                              contentType: response.Value.ContentType,
                                              sizeBytes: response.Value.ContentLength);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Blob not found: container '{Container}' path '{Path}'",
                                   containerName,
                                   blobPath);
                return null;
            }
        }


        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string containerName,
                                            string blobPath,
                                            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            if (response.Value)
            {
                _logger.LogInformation(
                    "Blob deleted: container '{Container}' path '{Path}'",
                    containerName, blobPath);
            }
            else
            {
                _logger.LogWarning(
                    "Blob not found for deletion: container '{Container}' path '{Path}'",
                    containerName, blobPath);
            }

            return response.Value;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string containerName,
                                            string blobPath,
                                            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            var response = await blobClient.ExistsAsync(cancellationToken);
            return response.Value;
        }


        /// <inheritdoc />
        public async Task<string> GenerateDownloadUrlAsync(string containerName,
                                                           string blobPath,
                                                           TimeSpan validity,
                                                           CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

            if (validity <= TimeSpan.Zero)
                throw new ArgumentException("Validity must be positive.", nameof(validity));

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            // Use User Delegation SAS for better security (no account key required)
            // Get a user delegation key that's valid for the requested period
            var startsOn = DateTimeOffset.UtcNow;
            var expiresOn = startsOn.Add(validity);

            var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                startsOn,
                expiresOn,
                cancellationToken);

            // Create a SAS token for read access
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobClient.Name,
                Resource = "b", // b = blob
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            // Build the SAS URI using the user delegation key
            var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
            {
                Sas = sasBuilder.ToSasQueryParameters(
                    userDelegationKey.Value,
                    _blobServiceClient.AccountName)
            };

            var sasUri = blobUriBuilder.ToUri().ToString();

            _logger.LogDebug(
                "Generated download URL for blob '{Path}' valid until {ExpiresOn}",
                blobPath, expiresOn);

            return sasUri;
        }
    }
}
