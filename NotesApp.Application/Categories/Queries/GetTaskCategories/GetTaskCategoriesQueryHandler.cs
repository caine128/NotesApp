using FluentResults;
using MediatR;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Categories.Models;
using NotesApp.Application.Common.Interfaces;
using System.Collections.Generic;

namespace NotesApp.Application.Categories.Queries.GetTaskCategories
{
    /// <summary>
    /// Returns all non-deleted task categories owned by the current user.
    /// This is a pure query — no outbox, no UnitOfWork.
    /// </summary>
    public sealed class GetTaskCategoriesQueryHandler
        : IRequestHandler<GetTaskCategoriesQuery, Result<IReadOnlyList<TaskCategoryDto>>>
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly ICurrentUserService _currentUserService;

        public GetTaskCategoriesQueryHandler(
            ICategoryRepository categoryRepository,
            ICurrentUserService currentUserService)
        {
            _categoryRepository = categoryRepository;
            _currentUserService = currentUserService;
        }

        public async Task<Result<IReadOnlyList<TaskCategoryDto>>> Handle(
            GetTaskCategoriesQuery request,
            CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            var categories = await _categoryRepository.GetAllForUserAsync(userId, cancellationToken);

            return Result.Ok(categories.ToDtoList());
        }
    }
}
