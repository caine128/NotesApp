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
using NotesApp.Application.Tasks.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TasksController : ControllerBase
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
        public async Task<ActionResult<TaskDto>> CreateTask([FromBody] CreateTaskCommand command,
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
        public async Task<ActionResult<TaskDto>> UpdateTask([FromRoute] Guid taskId,
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


        /// <summary>
        /// Get all tasks for a specific user and day.
        /// </summary>
        [HttpGet("day")]
        public async Task<ActionResult<IReadOnlyList<TaskDto>>> GetTasksForDay([FromQuery] DateOnly date,
                                                                               CancellationToken cancellationToken)
        {
            // TODO (later): ignore userId query parameter and derive it from JWT claims
            // Same pattern: query -> Result<IReadOnlyList<TaskDto>> -> ToActionResult()
            var query = new GetTasksForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
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


        [HttpGet("month-overview")]
        public async Task<ActionResult<IReadOnlyList<DayTasksOverviewDto>>> GetMonthOverview([FromQuery] int year,
                                                                                             [FromQuery] int month,
                                                                                             CancellationToken cancellationToken)
        {
            var query = new GetMonthOverviewQuery(year, month);

            var result = await _mediator.Send(query, cancellationToken);

            // Uses FluentResults.Extensions.AspNetCore + our ResultEndpointProfile
            // to map Result<T> -> ActionResult<T> + ProblemDetails.
            return result.ToActionResult();
        }


        /// <summary>
        /// Returns an overview of tasks per month for the authenticated user
        /// for the specified year. Each entry contains total, completed, and pending counts.
        /// </summary>
        [HttpGet("year-overview")]
        public async Task<ActionResult<IReadOnlyList<MonthTasksOverviewDto>>> GetYearOverview([FromQuery] int year,
                                                                                              CancellationToken cancellationToken)
        {
            var query = new GetYearOverviewQuery(year);

            var result = await _mediator.Send(query, cancellationToken);

            return result.ToActionResult();
        }



        /// <summary>
        /// Sets the completion state of a task (completed or pending).
        /// 
        /// This is a partial update (PATCH) because we only change the IsCompleted flag,
        /// not the whole task resource.
        /// </summary>
        [HttpPatch("{taskId:guid}/completion")]
        public async Task<ActionResult<TaskDto>> SetTaskCompletion(Guid taskId,
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
