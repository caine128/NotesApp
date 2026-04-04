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
        }
    }
}
