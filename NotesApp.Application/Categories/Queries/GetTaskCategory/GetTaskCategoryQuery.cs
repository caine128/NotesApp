using FluentResults;
using MediatR;
using NotesApp.Application.Categories.Models;
using System;

namespace NotesApp.Application.Categories.Queries.GetTaskCategory
{
    /// <summary>Returns a single task category by id, scoped to the current user.</summary>
    public sealed record GetTaskCategoryQuery(Guid CategoryId)
        : IRequest<Result<TaskCategoryDto>>;
}
