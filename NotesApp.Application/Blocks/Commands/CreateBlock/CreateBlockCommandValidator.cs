using FluentValidation;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.CreateBlock
{
    /// <summary>
    /// FluentValidation validator for CreateBlockCommand.
    /// 
    /// Validates:
    /// - Common fields (ParentId, ParentType, Type, Position)
    /// - Conditional fields based on block type (text vs asset blocks)
    /// 
    /// Uses constants from Block entity to ensure consistency with domain rules.
    /// </summary>
    public sealed class CreateBlockCommandValidator : AbstractValidator<CreateBlockCommand>
    {
        public CreateBlockCommandValidator()
        {
            // ─────────────────────────────────────────────────────────────────
            // Common required fields
            // ─────────────────────────────────────────────────────────────────

            RuleFor(x => x.ParentId)
                .NotEmpty()
                .WithMessage("ParentId is required.");

            RuleFor(x => x.ParentType)
                .IsInEnum()
                .WithMessage("ParentType must be Note. Tasks do not support blocks.")
                .Must(t => t == BlockParentType.Note)
                .WithMessage("Only Note is supported as parent type. Tasks do not have blocks.");

            RuleFor(x => x.Type)
                .IsInEnum()
                .WithMessage("Type must be a valid BlockType value.");

            RuleFor(x => x.Position)
                .NotEmpty()
                .WithMessage("Position is required.")
                .MaximumLength(Block.MaxPositionLength)
                .WithMessage($"Position must be at most {Block.MaxPositionLength} characters.");

            // ─────────────────────────────────────────────────────────────────
            // Asset block validation
            // ─────────────────────────────────────────────────────────────────

            When(x => Block.IsAssetBlockType(x.Type), () =>
            {
                RuleFor(x => x.AssetClientId)
                    .NotEmpty()
                    .WithMessage("AssetClientId is required for asset blocks.")
                    .MaximumLength(Block.MaxAssetClientIdLength)
                    .WithMessage($"AssetClientId must be at most {Block.MaxAssetClientIdLength} characters.");

                RuleFor(x => x.AssetFileName)
                    .NotEmpty()
                    .WithMessage("AssetFileName is required for asset blocks.")
                    .MaximumLength(Block.MaxAssetFileNameLength)
                    .WithMessage($"AssetFileName must be at most {Block.MaxAssetFileNameLength} characters.");

                RuleFor(x => x.AssetSizeBytes)
                    .NotNull()
                    .WithMessage("AssetSizeBytes is required for asset blocks.")
                    .GreaterThan(0)
                    .WithMessage("AssetSizeBytes must be a positive number.");

                RuleFor(x => x.AssetContentType)
                    .MaximumLength(Block.MaxAssetContentTypeLength)
                    .When(x => !string.IsNullOrEmpty(x.AssetContentType))
                    .WithMessage($"AssetContentType must be at most {Block.MaxAssetContentTypeLength} characters.");
            });

            // ─────────────────────────────────────────────────────────────────
            // Text block validation (no additional required fields beyond Position)
            // TextContent can be empty for text blocks (e.g., empty paragraph)
            // ─────────────────────────────────────────────────────────────────
        }
    }
}
