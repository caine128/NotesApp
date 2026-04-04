using FluentValidation;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Categories.Commands.CreateTaskCategory
{
    public sealed class CreateTaskCategoryCommandValidator
        : AbstractValidator<CreateTaskCategoryCommand>
    {
        public CreateTaskCategoryCommandValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Category name is required.")
                .MaximumLength(TaskCategory.MaxNameLength)
                .WithMessage($"Category name cannot exceed {TaskCategory.MaxNameLength} characters.");
        }
    }
}
