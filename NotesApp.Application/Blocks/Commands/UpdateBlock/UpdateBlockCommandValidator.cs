using FluentValidation;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.UpdateBlock
{
    /// <summary>
    /// FluentValidation validator for UpdateBlockCommand.
    /// 
    /// Validates:
    /// - BlockId is required
    /// - Position length (when provided)
    /// 
    /// Note: Whether TextContent is allowed depends on block type,
    /// which is checked at the handler level.
    /// </summary>
    public sealed class UpdateBlockCommandValidator : AbstractValidator<UpdateBlockCommand>
    {
        public UpdateBlockCommandValidator()
        {
            RuleFor(x => x.BlockId)
                .NotEmpty()
                .WithMessage("BlockId is required.");

            RuleFor(x => x.Position)
                .MaximumLength(Block.MaxPositionLength)
                .When(x => !string.IsNullOrEmpty(x.Position))
                .WithMessage($"Position must be at most {Block.MaxPositionLength} characters.");

            // TextContent has no length limit in the domain
            // Type-based validation (text vs asset) is done in the handler
        }
    }
}
