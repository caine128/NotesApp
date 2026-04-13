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


        }
    }
}
