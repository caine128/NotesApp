using FluentValidation;

namespace NotesApp.Application.Categories.Commands.DeleteTaskCategory
{
    public sealed class DeleteTaskCategoryCommandValidator
        : AbstractValidator<DeleteTaskCategoryCommand>
    {
        public DeleteTaskCategoryCommandValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("CategoryId is required.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
