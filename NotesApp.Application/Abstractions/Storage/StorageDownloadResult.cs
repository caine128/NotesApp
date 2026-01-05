using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Storage
{
    public sealed class StorageDownloadResult : IDisposable
    {
        public Stream Content { get; }
        public string ContentType { get; }
        public long SizeBytes { get; }

        public StorageDownloadResult(Stream content, string contentType, long sizeBytes)
        {
            Content = content;
            ContentType = contentType;
            SizeBytes = sizeBytes;
        }

        public void Dispose() => Content?.Dispose();
    }
}
