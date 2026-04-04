using FluentValidation;

namespace NotesApp.Application.Categories.Queries.GetTaskCategory
{
    public sealed class GetTaskCategoryQueryValidator
        : AbstractValidator<GetTaskCategoryQuery>
    {
        public GetTaskCategoryQueryValidator()
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("CategoryId is required.");
        }
    }
}
