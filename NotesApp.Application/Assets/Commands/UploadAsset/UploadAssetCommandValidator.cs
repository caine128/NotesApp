using FluentValidation;
using NotesApp.Application.Configuration;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Assets.Commands.UploadAsset
{
    /// <summary>
    /// FluentValidation validator for UploadAssetCommand.
    /// 
    /// Validates input fields that can be checked without database access:
    /// - BlockId: required
    /// - AssetClientId: required, max length
    /// - FileName: required, max length
    /// - ContentType: max length (optional field)
    /// - SizeBytes: positive, within max limit
    /// - Content: not null stream
    /// 
    /// Business validations that require database access remain in the handler:
    /// - Block exists and belongs to user
    /// - Block is an asset block type
    /// - Block has UploadStatus.Pending
    /// - AssetClientId matches block's expected value
    /// - Idempotency check (existing asset)
    /// 
    /// Note: Uses AssetStorageOptions.DefaultMaxFileSizeBytes for static validation.
    /// The handler may use a different configured value at runtime.
    /// </summary>
    public sealed class UploadAssetCommandValidator : AbstractValidator<UploadAssetCommand>
    {

        public UploadAssetCommandValidator()
        {
            // ─────────────────────────────────────────────────────────────────
            // BlockId - required
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.BlockId)
                .NotEmpty()
                .WithMessage("BlockId is required.");

            // ─────────────────────────────────────────────────────────────────
            // AssetClientId - required, max length
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.AssetClientId)
                .NotEmpty()
                .WithMessage("AssetClientId is required.")
                .MaximumLength(Block.MaxAssetClientIdLength)
                .WithMessage($"AssetClientId must be at most {Block.MaxAssetClientIdLength} characters.");

            // ─────────────────────────────────────────────────────────────────
            // FileName - required, max length
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.FileName)
                .NotEmpty()
                .WithMessage("FileName is required.")
                .MaximumLength(Block.MaxAssetFileNameLength)
                .WithMessage($"FileName must be at most {Block.MaxAssetFileNameLength} characters.");

            // ─────────────────────────────────────────────────────────────────
            // ContentType - optional but has max length
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.ContentType)
                .MaximumLength(Block.MaxAssetContentTypeLength)
                .When(x => !string.IsNullOrEmpty(x.ContentType))
                .WithMessage($"ContentType must be at most {Block.MaxAssetContentTypeLength} characters.");

            // ─────────────────────────────────────────────────────────────────
            // SizeBytes - must be positive and within limit
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.SizeBytes)
               .GreaterThan(0)
               .WithMessage("File size must be positive.")
               .LessThanOrEqualTo(AssetStorageOptions.DefaultMaxFileSizeBytes)
               .WithMessage($"File size exceeds maximum allowed size of {AssetStorageOptions.DefaultMaxFileSizeBytes / (1024 * 1024)} MB.");

            // ─────────────────────────────────────────────────────────────────
            // Content - must not be null stream
            // Note: We can't easily validate Stream.Length in validator because
            // the stream might not support seeking. SizeBytes is the declared size.
            // ─────────────────────────────────────────────────────────────────
            RuleFor(x => x.Content)
                .NotNull()
                .WithMessage("Content stream is required.")
                .Must(stream => stream != Stream.Null)
                .WithMessage("Content stream cannot be empty.");
        }
    }
}
