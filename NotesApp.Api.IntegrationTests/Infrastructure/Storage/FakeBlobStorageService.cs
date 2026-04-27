using FluentResults;
using NotesApp.Application.Abstractions.Storage;
using System.Collections.Concurrent;

namespace NotesApp.Api.IntegrationTests.Infrastructure.Storage
{
    /// <summary>
    /// In-memory fake for IBlobStorageService used in integration tests.
    /// Avoids the need for real Azure credentials while still exercising
    /// the full asset upload/download/sync code paths.
    /// </summary>
    public sealed class FakeBlobStorageService : IBlobStorageService
    {
        private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType)> _store = new();

        public async Task<Result<StorageUploadResult>> UploadAsync(
            string containerName,
            string blobPath,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            _store[blobPath] = (bytes, contentType);

            return Result.Ok(new StorageUploadResult(
                BlobPath: blobPath,
                ContentType: contentType,
                SizeBytes: bytes.LongLength,
                ETag: Guid.NewGuid().ToString()));
        }

        public Task<Result<StorageDownloadResult>> DownloadAsync(
            string containerName,
            string blobPath,
            CancellationToken cancellationToken = default)
        {
            if (!_store.TryGetValue(blobPath, out var entry))
            {
                return Task.FromResult(Result.Fail<StorageDownloadResult>("Blob.NotFound"));
            }

            return Task.FromResult(Result.Ok(
                new StorageDownloadResult(new MemoryStream(entry.Data), entry.ContentType, entry.Data.LongLength)));
        }

        public Task<Result<bool>> ExistsAsync(
            string containerName,
            string blobPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Ok(_store.ContainsKey(blobPath)));
        }

        public Task<Result> DeleteAsync(
            string containerName,
            string blobPath,
            CancellationToken cancellationToken = default)
        {
            _store.TryRemove(blobPath, out _);
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<string>> GenerateDownloadUrlAsync(
            string containerName,
            string blobPath,
            TimeSpan validity,
            CancellationToken cancellationToken = default)
        {
            var url = $"https://fake-storage.test/{containerName}/{blobPath}?expires={DateTime.UtcNow.Add(validity):O}";
            return Task.FromResult(Result.Ok(url));
        }
    }
}
