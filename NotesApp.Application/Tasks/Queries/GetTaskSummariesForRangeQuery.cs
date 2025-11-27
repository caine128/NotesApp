using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed record GetTaskSummariesForRangeQuery(DateOnly Start,
                                                       DateOnly EndExclusive) : IRequest<Result<IReadOnlyList<TaskSummaryDto>>>;
}
