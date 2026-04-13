using FluentValidation;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Categories.Commands.UpdateTaskCategory
{
    public sealed class UpdateTaskCategoryCommandValidator
        : AbstractValidator<UpdateTaskCategoryCommand>
    {
        public UpdateTaskCategoryCommandValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("CategoryId is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Category name is required.")
                .MaximumLength(TaskCategory.MaxNameLength)
                .WithMessage($"Category name cannot exceed {TaskCategory.MaxNameLength} characters.");

            // REFACTORED: RowVersion required for web concurrency protection
            RuleFor(x => x.RowVersion)
                .NotEmpty().WithMessage("RowVersion is required.")
                .Must(rv => rv.Length == 8).WithMessage("RowVersion must be 8 bytes.");
        }
    }
}
