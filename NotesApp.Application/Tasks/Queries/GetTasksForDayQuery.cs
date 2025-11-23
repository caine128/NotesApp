using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Queries
{
    public sealed record GetTasksForDayQuery(DateOnly Date) : IRequest<Result<IReadOnlyList<TaskDto>>>;
}
