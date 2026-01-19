using FluentResults;
using MediatR;
using NotesApp.Application.Assets.Models;
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


   
}
