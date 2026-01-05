using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Storage
{
    public sealed record StorageUploadResult(string BlobPath,
                                             string ContentType,
                                             long SizeBytes,
                                             string ETag);
}
