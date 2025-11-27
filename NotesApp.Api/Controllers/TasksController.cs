using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tasks.Commands.DeleteTask;
using NotesApp.Application.Tasks.Commands.SetTaskCompletion;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public sealed class TasksController : ControllerBase
    {
        private readonly IMediator _mediator;

        public TasksController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Create a new task for a specific day.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<TaskDetailDto>> CreateTask([FromBody] CreateTaskCommand command,
                                                            CancellationToken cancellationToken)
        {

            // IMediator returns Result<TaskDto> from the handler.
            // ToActionResult() uses NotesAppResultEndpointProfile to convert it to HTTP.
            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }


        /// <summary>
        /// Update an existing task's title, date and/or reminder.
        /// </summary>
        /// <param name="taskId">The id of the task to update (from the route).</param>
        /// <param name="command">The update payload (date, title, reminder).</param>
        [HttpPut("{taskId:guid}")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> UpdateTask([FromRoute] Guid taskId,
                                                            [FromBody] UpdateTaskCommand command,
                                                            CancellationToken cancellationToken)
        {
            // The route id is the single source of truth.
            // Even if the client sends a different TaskId in the body, we override it here.
            command.TaskId = taskId;

            var result = await _mediator.Send(command, cancellationToken);

            // Uses FluentResults.Extensions.AspNetCore:
            // - Success => 200 OK with TaskDto
            // - Failure => mapped ProblemDetails based on ErrorCode/metadata
            return result.ToActionResult();
        }

        [HttpGet("{taskId:guid}")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> GetTaskDetail(Guid taskId,
                                                                     CancellationToken cancellationToken)
        {
            var query = new GetTaskDetailQuery(taskId);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }


        /// <summary>
        /// Get all tasks for a specific user and day.
        /// </summary>
        [HttpGet("day")]
        [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<TaskSummaryDto>>> GetTaskSummariesForDay([FromQuery] DateOnly date,
                                                                               CancellationToken cancellationToken)
        {
            // TODO (later): ignore userId query parameter and derive it from JWT claims
            // Same pattern: query -> Result<IReadOnlyList<TaskDto>> -> ToActionResult()
            var query = new GetTaskSummariesForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Gets task summaries (timeline-level) for a date range.
        /// Useful for list views, custom ranges, etc.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        // GET /api/tasks/range?start=2025-11-01&end=2025-11-08
        [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [HttpGet("range")]
        public async Task<ActionResult<IReadOnlyList<TaskSummaryDto>>> GetTaskSummariesForRange(
                                                                         [FromQuery] DateOnly start,
                                                                         [FromQuery] DateOnly endExclusive,
                                                                         CancellationToken cancellationToken)
        {
            var query = new GetTaskSummariesForRangeQuery(start, endExclusive);
            return await _mediator.Send(query, cancellationToken).ToActionResult();
        }


        /// <summary>
        /// Gets task overviews (title + date only) for a date range.
        /// Typically used for month-like views or light-weight task lists.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        // GET /api/tasks/overview?start=...&end=...
        [HttpGet("overview")]
        [ProducesResponseType(typeof(IReadOnlyList<TaskOverviewDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<TaskOverviewDto>>> GetTaskOverviewForRange(
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly endExclusive,
            CancellationToken cancellationToken)
        {
            var query = new GetTaskOverviewForRangeQuery(start, endExclusive);
            return await _mediator.Send(query, cancellationToken).ToActionResult();
        }


        /// <summary>
        /// Soft-deletes a task for the current user.
        /// 
        /// Returns:
        /// - 204 NoContent on success
        /// - 404 NotFound if the task does not belong to the current user or does not exist
        /// - 400 / 500 via ProblemDetails for validation or unexpected errors.
        /// </summary>
        [HttpDelete("{taskId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteTask(Guid taskId,
                                                    CancellationToken cancellationToken)
        {
            var command = new DeleteTaskCommand
            {
                TaskId = taskId
            };

            // Result -> ActionResult mapping is handled by FluentResults.Extensions.AspNetCore
            // and our NotesAppResultEndpointProfile (Result.Ok -> 204 NoContent).
            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }


        /// <summary>
        /// Sets the completion state of a task (completed or pending).
        /// 
        /// This is a partial update (PATCH) because we only change the IsCompleted flag,
        /// not the whole task resource.
        /// </summary>
        [HttpPatch("{taskId:guid}/completion")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> SetTaskCompletion(Guid taskId,
                                                                   [FromBody] SetTaskCompletionRequest request,
                                                                   CancellationToken cancellationToken)
        {
            var command = new SetTaskCompletionCommand(taskId, request.IsCompleted);

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }



        /// <summary>
        /// Request payload used to set the completion state of a task.
        /// </summary>
        public sealed record SetTaskCompletionRequest(bool IsCompleted);
    }
}
