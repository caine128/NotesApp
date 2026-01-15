using FluentResults;
using MediatR;
using NotesApp.Application.Blocks.Models;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.CreateBlock
{
    /// <summary>
    /// Command to create a new block within a Note or Task.
    /// 
    /// Blocks come in two types:
    /// - Text blocks (Paragraph, Heading, etc.): require Position, optional TextContent
    /// - Asset blocks (Image, File): require Position and asset metadata (AssetClientId, AssetFileName, etc.)
    /// 
    /// The domain entity enforces which properties are required based on BlockType.
    /// </summary>
    public sealed class CreateBlockCommand : IRequest<Result<BlockDetailDto>>
    {
        /// <summary>
        /// Server ID of the parent Note or Task.
        /// </summary>
        public Guid ParentId { get; init; }

        /// <summary>
        /// Type of parent entity (Note or Task).
        /// </summary>
        public BlockParentType ParentType { get; init; }

        /// <summary>
        /// Type of this block (Paragraph, Image, etc.).
        /// </summary>
        public BlockType Type { get; init; }

        /// <summary>
        /// Fractional index for ordering blocks within parent.
        /// Lexicographically sortable string (e.g., "a0", "a1", "a0V").
        /// </summary>
        public string Position { get; init; } = string.Empty;

        /// <summary>
        /// Markdown text content for text-based blocks.
        /// Null for asset blocks.
        /// </summary>
        public string? TextContent { get; init; }

        /// <summary>
        /// Client-generated identifier for tracking asset upload.
        /// Required for asset blocks.
        /// </summary>
        public string? AssetClientId { get; init; }

        /// <summary>
        /// Original filename from client.
        /// Required for asset blocks.
        /// </summary>
        public string? AssetFileName { get; init; }

        /// <summary>
        /// MIME type (e.g., "image/jpeg").
        /// Optional for asset blocks.
        /// </summary>
        public string? AssetContentType { get; init; }

        /// <summary>
        /// File size in bytes.
        /// Required for asset blocks.
        /// </summary>
        public long? AssetSizeBytes { get; init; }
    }
}
