using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    /// <summary>
    /// Request for a month overview of tasks for the current authenticated user.
    /// </summary>
    /// <param name="Year">The year (e.g. 2025).</param>
    /// <param name="Month">The month number (1-12).</param>
    public sealed record GetMonthOverviewQuery(int Year, int Month)
        : IRequest<Result<IReadOnlyList<DayTasksOverviewDto>>>;
}
