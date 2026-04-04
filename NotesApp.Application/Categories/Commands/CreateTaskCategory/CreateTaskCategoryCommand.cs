using FluentResults;
using MediatR;
using NotesApp.Application.Categories.Models;

namespace NotesApp.Application.Categories.Commands.CreateTaskCategory
{
    /// <summary>
    /// Creates a new user-defined task category.
    /// UserId is resolved from the authenticated user's JWT claims inside the handler.
    /// </summary>
    public sealed class CreateTaskCategoryCommand : IRequest<Result<TaskCategoryDto>>
    {
        /// <summary>Display name for the new category (e.g. "Work", "Lifestyle").</summary>
        public string Name { get; init; } = string.Empty;
    }
}
