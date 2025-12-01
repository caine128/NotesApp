using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Notes;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Notes.Commands.DeleteNote;
using NotesApp.Application.Notes.Commands.UpdateNote;
using NotesApp.Application.Notes.Models;
using NotesApp.Application.Notes.Queries;

namespace NotesApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ApiScope")]
    [Authorize]
    public sealed class NotesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public NotesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Creates a new note for the current user and the specified date.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(NoteDetailDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<NoteDetailDto>> CreateNote([FromBody] CreateNoteCommand command,
                                                            CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                return result.ToActionResult();
            }

            var dto = result.Value;

            return CreatedAtAction(
                nameof(GetNoteDetail),
                new { noteId = dto.NoteId },   // NoteDetailDto.NoteId
                dto);
        }

        [HttpGet("{noteId:guid}")]
        [ProducesResponseType(typeof(NoteDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<NoteDetailDto>> GetNoteDetail(Guid noteId,
                                                             CancellationToken cancellationToken)
        {
            var query = new GetNoteDetailQuery(noteId);
            return await _mediator.Send(query, cancellationToken)
                                  .ToActionResult();
        }


        /// <summary>
        /// Returns all notes for the current user on a given date.
        /// </summary>
        [HttpGet("day")]
        [ProducesResponseType(typeof(IReadOnlyList<NoteSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<NoteSummaryDto>>> GetNoteSummariesForDay([FromQuery] DateOnly date,
                                                                               CancellationToken cancellationToken)
        {
            var query = new GetNoteSummariesForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }


        /// <summary>
        /// Gets note summaries for a date range.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        // GET /api/notes/range?start=...&end=...
        [HttpGet("range")]
        [ProducesResponseType(typeof(IReadOnlyList<NoteSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<NoteSummaryDto>>> GetNoteSummariesForRange(
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly endExclusive,
            CancellationToken cancellationToken)
        {
            var query = new GetNoteSummariesForRangeQuery(start, endExclusive);
            return await _mediator.Send(query, cancellationToken).ToActionResult();
        }

        /// <summary>
        /// Gets note overviews (title + date only) for a date range.
        /// Typically used for month-like views.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        // GET /api/notes/overview?start=...&end=...
        [HttpGet("overview")]
        [ProducesResponseType(typeof(IReadOnlyList<NoteOverviewDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<NoteOverviewDto>>> GetNoteOverviewForRange(
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly endExclusive,
            CancellationToken cancellationToken)
        {
            var query = new GetNoteOverviewForRangeQuery(start, endExclusive);
            return await _mediator.Send(query, cancellationToken).ToActionResult();
        }


        [HttpPut("{noteId:guid}")]
        [ProducesResponseType(typeof(NoteDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<NoteDetailDto>> UpdateNote(Guid noteId,
                                                                  [FromBody] UpdateNoteCommand command,
                                                                  CancellationToken cancellationToken)
        {
            command.NoteId = noteId;

            return await _mediator
                .Send(command, cancellationToken)
                .ToActionResult();
        }

        [HttpDelete("{noteId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteNote(Guid noteId,
                                                    CancellationToken cancellationToken)
        {
            var command = new DeleteNoteCommand(noteId);

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailed)
            {
                // Uses ErrorCode = "Notes.NotFound" for 404.
                return result.ToActionResult();
            }

            return NoContent();
        }
    }
}
