using FluentResults.Extensions.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotesApp.Application.Calendar.Models;
using NotesApp.Application.Calendar.Queries;

namespace NotesApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public sealed class CalendarController : ControllerBase
    {
        private readonly IMediator _mediator;

        public CalendarController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Gets the calendar summary (tasks + notes) for a single day.
        /// </summary>
        /// <param name="date">The date to fetch (local user date).</param>
        [HttpGet("summary/day")]
        [ProducesResponseType(typeof(CalendarSummaryDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CalendarSummaryDto>> GetCalendarSummaryForDay(
            [FromQuery] DateOnly date,
            CancellationToken cancellationToken)
        {
            var query = new CalendarSummaryForDayQuery(date);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Gets the calendar summaries (tasks + notes) for a date range.
        /// Useful for day, 3-day, week views, etc.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        [HttpGet("summary/range")]
        [ProducesResponseType(typeof(IReadOnlyList<CalendarSummaryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<CalendarSummaryDto>>> GetCalendarSummaryForRange(
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly endExclusive,
            CancellationToken cancellationToken)
        {
            var query = new CalendarSummaryForRangeQuery(start, endExclusive);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }

        /// <summary>
        /// Gets the calendar overview (tasks + notes, titles only) for a date range.
        /// Typically used for month views, but accepts any range.
        /// </summary>
        /// <param name="start">Inclusive start date.</param>
        /// <param name="endExclusive">Exclusive end date.</param>
        [HttpGet("overview/range")]
        [ProducesResponseType(typeof(IReadOnlyList<CalendarOverviewDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IReadOnlyList<CalendarOverviewDto>>> GetCalendarOverviewForRange(
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly endExclusive,
            CancellationToken cancellationToken)
        {
            var query = new CalendarOverviewForRangeQuery(start, endExclusive);

            return await _mediator
                .Send(query, cancellationToken)
                .ToActionResult();
        }
    }
}
