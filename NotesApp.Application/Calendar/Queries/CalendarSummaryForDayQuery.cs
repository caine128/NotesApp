using FluentResults;
using MediatR;
using NotesApp.Application.Calendar.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Queries
{
    public sealed record CalendarSummaryForDayQuery(DateOnly Date)
    : IRequest<Result<CalendarSummaryDto>>;
}
