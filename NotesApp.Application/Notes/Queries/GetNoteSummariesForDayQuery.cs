using FluentResults;
using MediatR;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    /// <summary>
    /// Query to fetch all notes for the current user on a given calendar date.
    /// </summary>
    public sealed record GetNoteSummariesForDayQuery(DateOnly Date)
        : IRequest<Result<IReadOnlyList<NoteSummaryDto>>>;
}
