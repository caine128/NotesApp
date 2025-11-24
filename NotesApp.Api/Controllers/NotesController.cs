using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Notes;
using NotesApp.Application.Notes.Commands.CreateNote;
using NotesApp.Application.Notes.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
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
        [ProducesResponseType(typeof(NoteDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<NoteDto>> CreateNote([FromBody] CreateNoteCommand command,
                                                            CancellationToken cancellationToken)
        {
            // MediatR + Result pattern:
            // Command → Result<NoteDto> → ToActionResult() → ProblemDetails/201/etc.
            return await _mediator.Send(command, cancellationToken)
                                        .ToActionResult();
        }

        /// <summary>
        /// Returns all notes for the current user on a given date.
        /// </summary>
        [HttpGet("day")]
        [ProducesResponseType(typeof(IReadOnlyList<NoteDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<NoteDto>>> GetNotesForDay(
            [FromQuery] DateOnly date,
            CancellationToken cancellationToken)
        {
            var query = new GetNotesForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }
    }
}
