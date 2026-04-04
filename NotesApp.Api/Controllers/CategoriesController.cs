using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Categories.Commands.CreateTaskCategory;
using NotesApp.Application.Categories.Commands.DeleteTaskCategory;
using NotesApp.Application.Categories.Commands.UpdateTaskCategory;
using NotesApp.Application.Categories.Models;
using NotesApp.Application.Categories.Queries.GetTaskCategories;
using NotesApp.Application.Categories.Queries.GetTaskCategory;

namespace NotesApp.Api.Controllers
{
    /// <summary>
    /// Manages user-defined task categories.
    ///
    /// Categories are per-user, named labels (e.g. Work, Health, Lifestyle) that
    /// can be assigned to a task. Each task supports at most one optional category.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public sealed class CategoriesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public CategoriesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Creates a new task category for the current user.
        /// </summary>
        /// <returns>201 Created with the new category and a Location header.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(TaskCategoryDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<TaskCategoryDto>> CreateCategory(
            [FromBody] CreateTaskCategoryCommand command,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            var dto = result.Value;

            // 201 Created + Location header pointing to GET /api/categories/{categoryId}
            return CreatedAtAction(
                nameof(GetCategoryDetail),
                new { categoryId = dto.CategoryId },
                dto);
        }

        /// <summary>
        /// Returns all non-deleted categories belonging to the current user.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<TaskCategoryDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<TaskCategoryDto>>> GetCategories(
            CancellationToken cancellationToken)
        {
            var query = new GetTaskCategoriesQuery();

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Returns the details of a single category.
        /// </summary>
        /// <param name="categoryId">The id of the category to retrieve.</param>
        [HttpGet("{categoryId:guid}")]
        [ProducesResponseType(typeof(TaskCategoryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskCategoryDto>> GetCategoryDetail(
            Guid categoryId,
            CancellationToken cancellationToken)
        {
            var query = new GetTaskCategoryQuery(categoryId);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Renames an existing category.
        /// </summary>
        /// <param name="categoryId">The id of the category to update (from the route).</param>
        /// <param name="command">The update payload.</param>
        [HttpPut("{categoryId:guid}")]
        [ProducesResponseType(typeof(TaskCategoryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskCategoryDto>> UpdateCategory(
            [FromRoute] Guid categoryId,
            [FromBody] UpdateTaskCategoryCommand command,
            CancellationToken cancellationToken)
        {
            // Route id is the single source of truth — override any body value.
            command.CategoryId = categoryId;

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Soft-deletes a category and clears it from all tasks that reference it.
        ///
        /// After deletion, all tasks that belonged to this category will have
        /// CategoryId = null. They will surface in the next sync pull as updated tasks.
        /// </summary>
        /// <param name="categoryId">The id of the category to delete.</param>
        [HttpDelete("{categoryId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCategory(
            Guid categoryId,
            CancellationToken cancellationToken)
        {
            var command = new DeleteTaskCategoryCommand { CategoryId = categoryId };

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            // 204 NoContent on success (idempotent — already-deleted returns 204 too)
            return NoContent();
        }
    }
}
