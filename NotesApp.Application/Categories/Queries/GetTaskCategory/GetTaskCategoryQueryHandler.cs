using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Models;
using NotesApp.Application.Common.Interfaces;

namespace NotesApp.Application.Categories.Queries.GetTaskCategory
{
    /// <summary>
    /// Returns a single task category by id.
    /// Returns NotFound for both missing and wrong-user categories to prevent
    /// information leakage.
    /// </summary>
    public sealed class GetTaskCategoryQueryHandler
        : IRequestHandler<GetTaskCategoryQuery, Result<TaskCategoryDto>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetTaskCategoryQueryHandler(
            ICategoryRepository categoryRepository,
            ICurrentUserService currentUserService)
        {
            _categoryRepository = categoryRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<TaskCategoryDto>> Handle(
            GetTaskCategoryQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var category = await _categoryRepository.GetByIdUntrackedAsync(
                request.CategoryId, cancellationToken);

            if (category is null || category.UserId != userId)
            {
                return Result.Fail<TaskCategoryDto>(
                    new Error("Category not found.")
                        .WithMetadata("ErrorCode", "Categories.NotFound"));
            }

            return Result.Ok(category.ToDto());
        }
    }
}
