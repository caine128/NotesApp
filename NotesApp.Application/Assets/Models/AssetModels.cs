using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Assets.Models
{
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
        /// 
        /// May be null if URL generation failed transiently after the asset was 
        /// successfully created. In this case, the client should fetch the URL 
        /// separately using the GetAssetDownloadUrl endpoint or sync pull.
        /// </summary>
        public string? DownloadUrl { get; init; }
    }
}
