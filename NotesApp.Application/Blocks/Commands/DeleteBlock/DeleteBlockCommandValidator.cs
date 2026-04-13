using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Blocks.Commands.DeleteBlock
{
    /// <summary>
    /// Validates the DeleteBlockCommand before it reaches the handler.
    /// </summary>
    public sealed class DeleteBlockCommandValidator : AbstractValidator<DeleteBlockCommand>
    {
        public DeleteBlockCommandValidator()
        {
            RuleFor(x => x.BlockId)
                .NotEmpty()
                .WithMessage("BlockId is required.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
