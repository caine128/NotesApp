using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Storage
{
    /// <summary>
    /// Vendor-agnostic blob storage abstraction.
    /// 
    /// This interface provides a clean abstraction over cloud blob storage,
    /// allowing the application to remain independent of specific providers
    /// (Azure Blob Storage, AWS S3, etc.).
    /// 
    /// Implementation should use the provider's SDK with proper:
    /// - Async/await patterns
    /// - CancellationToken support
    /// - Error handling for transient failures
    /// </summary>
    public interface IBlobStorageService
    {
        /// <summary>
        /// Uploads content to blob storage.
        /// </summary>
        /// <param name="containerName">Name of the container/bucket.</param>
        /// <param name="blobPath">Path within the container (e.g., "userId/parentId/blockId/filename").</param>
        /// <param name="content">Stream containing the content to upload.</param>
        /// <param name="contentType">MIME type of the content (e.g., "image/jpeg").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result containing blob metadata on success.</returns>
        Task<BlobUploadResult> UploadAsync(string containerName,
                                           string blobPath,
                                           Stream content,
                                           string contentType,
                                           CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a blob as a stream.
        /// </summary>
        /// <param name="containerName">Name of the container/bucket.</param>
        /// <param name="blobPath">Path within the container.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Stream containing the blob content, or null if blob doesn't exist.</returns>
        Task<BlobDownloadResult?> DownloadAsync(string containerName,
                                                string blobPath,
                                                CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a blob from storage.
        /// </summary>
        /// <param name="containerName">Name of the container/bucket.</param>
        /// <param name="blobPath">Path within the container.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if deleted, false if blob didn't exist.</returns>
        Task<bool> DeleteAsync(string containerName,
                               string blobPath,
                               CancellationToken cancellationToken = default);



        /// <summary>
        /// Checks if a blob exists.
        /// </summary>
        /// <param name="containerName">Name of the container/bucket.</param>
        /// <param name="blobPath">Path within the container.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if blob exists, false otherwise.</returns>
        Task<bool> ExistsAsync(string containerName,
                               string blobPath,
                               CancellationToken cancellationToken = default);



        /// <summary>
        /// Generates a time-limited download URL for a blob.
        /// For Azure, this generates a SAS URL. For S3, a presigned URL.
        /// </summary>
        /// <param name="containerName">Name of the container/bucket.</param>
        /// <param name="blobPath">Path within the container.</param>
        /// <param name="validity">How long the URL should be valid.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Time-limited URL for downloading the blob.</returns>
        Task<string> GenerateDownloadUrlAsync(string containerName,
                                              string blobPath,
                                              TimeSpan validity,
                                              CancellationToken cancellationToken = default);


        /// <summary>
        /// Result of a successful blob upload operation.
        /// </summary>
        public sealed class BlobUploadResult
        {
            /// <summary>
            /// Path where the blob was stored.
            /// </summary>
            public string BlobPath { get; }

            /// <summary>
            /// Content type of the uploaded blob.
            /// </summary>
            public string ContentType { get; }

            /// <summary>
            /// Size of the uploaded blob in bytes.
            /// </summary>
            public long SizeBytes { get; }

            /// <summary>
            /// ETag for the uploaded blob (for concurrency control).
            /// </summary>
            public string ETag { get; }

            public BlobUploadResult(string blobPath, string contentType, long sizeBytes, string eTag)
            {
                BlobPath = blobPath;
                ContentType = contentType;
                SizeBytes = sizeBytes;
                ETag = eTag;
            }
        }

        /// <summary>
        /// Result of a successful blob download operation.
        /// </summary>
        public sealed class BlobDownloadResult : IDisposable
        {
            /// <summary>
            /// Stream containing the blob content.
            /// Caller is responsible for disposing.
            /// </summary>
            public Stream Content { get; }

            /// <summary>
            /// Content type of the blob.
            /// </summary>
            public string ContentType { get; }

            /// <summary>
            /// Size of the blob in bytes.
            /// </summary>
            public long SizeBytes { get; }

            public BlobDownloadResult(Stream content, string contentType, long sizeBytes)
            {
                Content = content;
                ContentType = contentType;
                SizeBytes = sizeBytes;
            }

            public void Dispose()
            {
                Content?.Dispose();
            }
        }
    }
}
