using FluentResults;
using MediatR;
using NotesApp.Application.Calendar.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed record CalendarOverviewForRangeQuery(
    DateOnly Start,
    DateOnly EndExclusive
) : IRequest<Result<IReadOnlyList<CalendarOverviewDto>>>;
}
