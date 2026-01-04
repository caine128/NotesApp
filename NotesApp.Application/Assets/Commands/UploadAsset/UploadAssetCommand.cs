using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Assets.Commands.UploadAsset
{
    /// <summary>
    /// Command to upload an asset (binary file) for a block.
    /// The block must already exist with UploadStatus.Pending.
    /// </summary>
    public sealed class UploadAssetCommand : IRequest<Result<UploadAssetResultDto>>
    {
        /// <summary>
        /// ID of the block this asset belongs to.
        /// </summary>
        public Guid BlockId { get; init; }

        /// <summary>
        /// Client-generated asset identifier (must match block's AssetClientId).
        /// </summary>
        public string AssetClientId { get; init; } = string.Empty;

        /// <summary>
        /// Stream containing the file content.
        /// </summary>
        public Stream Content { get; init; } = Stream.Null;

        /// <summary>
        /// Original filename.
        /// </summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>
        /// MIME type of the content.
        /// </summary>
        public string ContentType { get; init; } = "application/octet-stream";

        /// <summary>
        /// File size in bytes (for validation).
        /// </summary>
        public long SizeBytes { get; init; }
    }


    /// <summary>
    /// Result of a successful asset upload.
    /// </summary>
    public sealed record UploadAssetResultDto
    {
        /// <summary>
        /// Server-generated asset ID.
        /// </summary>
        public Guid AssetId { get; init; }

        /// <summary>
        /// Block ID the asset is linked to.
        /// </summary>
        public Guid BlockId { get; init; }

        /// <summary>
        /// Pre-signed download URL for the uploaded asset.
        /// </summary>
        public string DownloadUrl { get; init; } = string.Empty;
    }
}
