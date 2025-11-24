using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    /// <summary>
    /// Request for a year overview of tasks for the current authenticated user.
    /// </summary>
    /// <param name="Year">The year (e.g. 2025).</param>
    public sealed record GetYearOverviewQuery(int Year)
        : IRequest<Result<IReadOnlyList<MonthTasksOverviewDto>>>;
}
