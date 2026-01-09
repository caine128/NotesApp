using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Storage
{
    /// <summary>
    /// Centralized constants for blob storage operations.
    /// </summary>
    public static class StorageConstants
    {
        /// <summary>
        /// Default MIME type for binary content when no specific type is provided.
        /// </summary>
        public const string DefaultContentType = "application/octet-stream";
    }
}
