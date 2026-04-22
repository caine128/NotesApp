using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Subtasks.Commands.CreateSubtask;
using NotesApp.Application.Subtasks.Commands.DeleteSubtask;
using NotesApp.Application.Subtasks.Commands.UpdateSubtask;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Commands.AcknowledgeReminder;
using NotesApp.Application.Tasks.Commands.CreateTask;
using NotesApp.Application.Tasks.Commands.DeleteRecurringTaskOccurrence;
using NotesApp.Application.Tasks.Commands.DeleteTask;
using NotesApp.Application.Tasks.Commands.SetTaskCompletion;
using NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrence;
using NotesApp.Application.Tasks.Commands.UpdateTask;
using NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrenceSubtasks;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Queries;

namespace NotesApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ApiScope")]
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

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                // Let the profile + GlobalExceptionHandler do their job.
                return result.ToActionResult();
            }

            var dto = result.Value;

            // 201 Created + Location header pointing to GET /api/tasks/{taskId}
            return CreatedAtAction(
                nameof(GetTaskDetail),
                new { taskId = dto.TaskId },   // TaskDetailDto.TaskId
                dto);
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
        [ProducesResponseType(StatusCodes.Status409Conflict)] // REFACTORED: web concurrency protection
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
        [ProducesResponseType(StatusCodes.Status409Conflict)] // REFACTORED: web concurrency protection
        public async Task<IActionResult> DeleteTask([FromRoute] Guid taskId,
                                                    [FromBody] DeleteTaskCommand command,
                                                    CancellationToken cancellationToken)
        {
            command.TaskId = taskId;

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                // This will map Tasks.NotFound to 404, others to 400/500.
                return result.ToActionResult();
            }

            // Successful soft-delete => 204 NoContent
            return NoContent();
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
        [ProducesResponseType(StatusCodes.Status409Conflict)] // REFACTORED: web concurrency protection
        public async Task<ActionResult<TaskDetailDto>> SetTaskCompletion([FromRoute] Guid taskId,
                                                                         [FromBody] SetTaskCompletionCommand command,
                                                                         CancellationToken cancellationToken)
        {
            command.TaskId = taskId;

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Acknowledges a reminder for the specified task.
        /// </summary>
        [HttpPost("{taskId:guid}/reminder/acknowledge")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AcknowledgeReminder(Guid taskId,
                                                             [FromBody] AcknowledgeTaskReminderRequestDto request,
                                                             CancellationToken cancellationToken)
        {
            var command = new AcknowledgeTaskReminderCommand
            {
                TaskId = taskId,
                DeviceId = request.DeviceId,
                AcknowledgedAtUtc = request.AcknowledgedAtUtc
            };

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                // Map domain/app errors to proper ProblemDetails like other endpoints
                return result.ToActionResult();
            }

            // Successful acknowledgment => 204 NoContent (no body needed)
            return NoContent();
        }

        // -----------------------------------------------------------------------
        // Subtask endpoints (nested under /api/tasks/{taskId}/subtasks)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Creates a new subtask within the specified task.
        ///
        /// The caller must supply a Position using the fractional-index format
        /// (e.g. "a0", "a1", "a0V") computed with the same algorithm used by the
        /// mobile client (e.g. @rocicorp/fractional-indexing).
        /// </summary>
        [HttpPost("{taskId:guid}/subtasks")]
        [ProducesResponseType(typeof(SubtaskDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SubtaskDto>> CreateSubtask(
            [FromRoute] Guid taskId,
            [FromBody] CreateSubtaskCommand command,
            CancellationToken cancellationToken)
        {
            command.TaskId = taskId;

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            var dto = result.Value;

            // 201 Created — no dedicated GET-by-subtask-id endpoint; return dto inline.
            return CreatedAtAction(
                nameof(GetTaskDetail),
                new { taskId },
                dto);
        }

        /// <summary>
        /// Updates an existing subtask.
        /// All body fields are optional — omit (or send null) to leave a field unchanged.
        /// </summary>
        [HttpPut("{taskId:guid}/subtasks/{subtaskId:guid}")]
        [ProducesResponseType(typeof(SubtaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)] // REFACTORED: web concurrency protection
        public async Task<ActionResult<SubtaskDto>> UpdateSubtask(
            [FromRoute] Guid taskId,
            [FromRoute] Guid subtaskId,
            [FromBody] UpdateSubtaskCommand command,
            CancellationToken cancellationToken)
        {
            command.TaskId = taskId;
            command.SubtaskId = subtaskId;

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Soft-deletes a subtask.
        /// </summary>
        [HttpDelete("{taskId:guid}/subtasks/{subtaskId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)] // REFACTORED: web concurrency protection
        public async Task<IActionResult> DeleteSubtask(
            [FromRoute] Guid taskId,
            [FromRoute] Guid subtaskId,
            [FromBody] DeleteSubtaskCommand command,
            CancellationToken cancellationToken)
        {
            command.TaskId = taskId;
            command.SubtaskId = subtaskId;

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            return NoContent();
        }

        // -----------------------------------------------------------------------
        // Recurring task endpoints
        // REFACTORED: added for recurring-tasks feature
        // -----------------------------------------------------------------------

        /// <summary>
        /// Updates a materialized recurring task occurrence.
        /// Scope controls how many occurrences are affected (Single / ThisAndFollowing / All).
        /// </summary>
        /// <param name="taskId">Id of the materialized TaskItem being updated.</param>
        /// <param name="command">Update payload including Scope, SeriesId, OccurrenceDate and task fields.</param>
        [HttpPut("{taskId:guid}/recurring")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> UpdateRecurringTaskOccurrence(
            [FromRoute] Guid taskId,
            [FromBody] UpdateRecurringTaskOccurrenceCommand command,
            CancellationToken cancellationToken)
        {
            // Route taskId is the source of truth for the materialized TaskItemId.
            command.TaskItemId = taskId;

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Updates a virtual (non-materialized) recurring task occurrence.
        /// Scope controls how many occurrences are affected (Single / ThisAndFollowing / All).
        /// TaskItemId in the body must be null for virtual occurrences.
        /// </summary>
        [HttpPut("virtual-occurrences")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> UpdateVirtualOccurrence(
            [FromBody] UpdateRecurringTaskOccurrenceCommand command,
            CancellationToken cancellationToken)
        {
            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Deletes a materialized recurring task occurrence.
        /// Scope controls how many occurrences are deleted (Single / ThisAndFollowing / All).
        /// </summary>
        /// <param name="taskId">Id of the materialized TaskItem to delete.</param>
        /// <param name="command">Delete payload including Scope, SeriesId, and OccurrenceDate.</param>
        [HttpDelete("{taskId:guid}/recurring")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteRecurringTaskOccurrence(
            [FromRoute] Guid taskId,
            [FromBody] DeleteRecurringTaskOccurrenceCommand command,
            CancellationToken cancellationToken)
        {
            command.TaskItemId = taskId;

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            return NoContent();
        }

        /// <summary>
        /// Deletes a virtual (non-materialized) recurring task occurrence.
        /// Scope controls how many occurrences are deleted (Single / ThisAndFollowing / All).
        /// </summary>
        [HttpDelete("virtual-occurrences")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteVirtualOccurrence(
            [FromBody] DeleteRecurringTaskOccurrenceCommand command,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            return NoContent();
        }

        /// <summary>
        /// Returns the full detail of a virtual (non-materialized) recurring occurrence.
        /// Use this when the client taps on an occurrence with IsVirtualOccurrence = true.
        /// </summary>
        /// <param name="seriesId">The series that owns the occurrence.</param>
        /// <param name="date">The canonical (engine-generated) occurrence date.</param>
        [HttpGet("virtual-occurrences/detail")]
        [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<TaskDetailDto>> GetVirtualOccurrenceDetail(
            [FromQuery] Guid seriesId,
            [FromQuery] DateOnly date,
            CancellationToken cancellationToken)
        {
            var query = new GetVirtualTaskOccurrenceDetailQuery
            {
                SeriesId = seriesId,
                OccurrenceDate = date
            };

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Replaces the complete subtask list for a recurring task occurrence according to the specified scope.
        /// Works for both materialized and virtual occurrences — provide TaskItemId for materialized, null for virtual.
        /// The client always sends the full desired state (full replace, not a patch).
        /// Scope controls how many occurrences are affected (Single / ThisAndFollowing / All).
        /// </summary>
        [HttpPut("recurring-occurrences/subtasks")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateRecurringOccurrenceSubtasks(
            [FromBody] UpdateRecurringTaskOccurrenceSubtasksCommand command,
            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            return NoContent();
        }
    }
}
