using FluentResults;
using MediatR;
using NotesApp.Application.Categories.Models;
using System;

namespace NotesApp.Application.Categories.Commands.UpdateTaskCategory
{
    /// <summary>
    /// Renames an existing task category owned by the current user.
    /// CategoryId is populated from the route parameter by the controller.
    /// </summary>
    public sealed class UpdateTaskCategoryCommand : IRequest<Result<TaskCategoryDto>>
    {
        /// <summary>The server-side id of the category to rename (set from route).</summary>
        public Guid CategoryId { get; set; }

        /// <summary>The new display name for the category.</summary>
        public string Name { get; init; } = string.Empty;
    }
}
