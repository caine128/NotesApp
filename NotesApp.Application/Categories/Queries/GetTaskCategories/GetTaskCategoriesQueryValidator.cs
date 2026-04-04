using FluentValidation;

namespace NotesApp.Application.Categories.Queries.GetTaskCategories
{
    /// <summary>
    /// Validator for GetTaskCategoriesQuery.
    /// The query has no parameters to validate; this class exists to maintain
    /// consistency with the MediatR validation pipeline.
    /// </summary>
    public sealed class GetTaskCategoriesQueryValidator
        : AbstractValidator<GetTaskCategoriesQuery>
    {
        public GetTaskCategoriesQueryValidator() { }
    }
}
