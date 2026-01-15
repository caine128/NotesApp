using NotesApp.Application.Blocks.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks
{
    /// <summary>
    /// Extension methods for mapping Block domain entities to DTOs.
    /// </summary>
    public static class BlockMappings
    {
        /// <summary>
        /// Maps a Block domain entity to a BlockDetailDto.
        /// </summary>
        public static BlockDetailDto ToDetailDto(this Block block)
        {
            return new BlockDetailDto
            {
                Id = block.Id,
                ParentId = block.ParentId,
                ParentType = block.ParentType,
                Type = block.Type,
                Position = block.Position,
                TextContent = block.TextContent,
                AssetId = block.AssetId,
                AssetClientId = block.AssetClientId,
                AssetFileName = block.AssetFileName,
                AssetContentType = block.AssetContentType,
                AssetSizeBytes = block.AssetSizeBytes,
                UploadStatus = block.UploadStatus,
                Version = block.Version,
                CreatedAtUtc = block.CreatedAtUtc,
                UpdatedAtUtc = block.UpdatedAtUtc
            };
        }

        /// <summary>
        /// Maps a collection of Block entities to BlockDetailDto list.
        /// </summary>
        public static IReadOnlyList<BlockDetailDto> ToDetailDtos(this IEnumerable<Block> blocks)
        {
            return blocks.Select(b => b.ToDetailDto()).ToList();
        }
    }
}
