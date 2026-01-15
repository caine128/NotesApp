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
        }
    }
}
