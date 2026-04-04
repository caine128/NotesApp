using FluentResults;
using MediatR;
using NotesApp.Application.Categories.Models;
using System.Collections.Generic;

namespace NotesApp.Application.Categories.Queries.GetTaskCategories
{
    /// <summary>
    /// Returns all non-deleted task categories belonging to the current user,
    /// ordered alphabetically by name.
    /// </summary>
    public sealed record GetTaskCategoriesQuery : IRequest<Result<IReadOnlyList<TaskCategoryDto>>>;
}
