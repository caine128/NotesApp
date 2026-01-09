using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Storage
{
    /// <summary>
    /// Result of a successful blob upload operation.
    /// </summary>
    /// <param name="BlobPath">Path where the blob was stored.</param>
    /// <param name="ContentType">Content type of the uploaded blob.</param>
    /// <param name="SizeBytes">Size of the uploaded blob in bytes.</param>
    /// <param name="ETag">ETag for the uploaded blob (for concurrency control).</param>
    public sealed record StorageUploadResult(string BlobPath,
                                             string ContentType,
                                             long SizeBytes,
                                             string ETag);
}
