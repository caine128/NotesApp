using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed record GetTaskSummariesForDayQuery(DateOnly Date) 
        : IRequest<Result<IReadOnlyList<TaskSummaryDto>>>;
}
