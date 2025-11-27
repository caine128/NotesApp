using FluentResults;
using MediatR;
using NotesApp.Application.Notes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Queries
{
    public sealed record GetNoteSummariesForRangeQuery(DateOnly Start,
                                                       DateOnly EndExclusive) 
        : IRequest<Result<IReadOnlyList<NoteSummaryDto>>>;
}
