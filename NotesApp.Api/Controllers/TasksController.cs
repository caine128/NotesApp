using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Commands.CreateTask;
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
        public async Task<ActionResult<TaskDto>> CreateTask(
            [FromBody] CreateTaskCommand command,
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
        public async Task<ActionResult<IReadOnlyList<TaskDto>>> GetTasksForDay(
            [FromQuery] DateOnly date,
            CancellationToken cancellationToken)
        {
            // TODO (later): ignore userId query parameter and derive it from JWT claims
            // Same pattern: query -> Result<IReadOnlyList<TaskDto>> -> ToActionResult()
            var query = new GetTasksForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }
    }
}
