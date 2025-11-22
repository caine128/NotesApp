using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tasks.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
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
            // TODO : 
            // Today: UserId is in the command body.
            // Later: we’ll populate it from JWT claims here and ignore the body’s UserId.

            // IMediator returns Result<TaskDto> from the handler.
            // ToActionResult() uses NotesAppResultEndpointProfile to convert it to HTTP.
            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Get all tasks for a specific user and day.
        /// </summary>
        [HttpGet("day")]
        public async Task<ActionResult<IReadOnlyList<TaskDto>>> GetTasksForDay(
            [FromQuery] Guid userId,
            [FromQuery] DateOnly date,
            CancellationToken cancellationToken)
        {
            // TODO (later): ignore userId query parameter and derive it from JWT claims
            // Same pattern: query -> Result<IReadOnlyList<TaskDto>> -> ToActionResult()
            var query = new GetTasksForDayQuery(userId, date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }
    }
}
