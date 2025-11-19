using FluentResults;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Commands.CreateTask
{
    /// <summary>
    /// Command to create a new task for a given user and date.
    /// In the real app, UserId will come from the authenticated user (JWT),
    /// but for now we keep it as a parameter.
    /// </summary>
    // TODO : In the real app, UserId will come from the authenticated user (JWT),

    public sealed class CreateTaskCommand : IRequest<Result<TaskDto>>
    {
        public Guid UserId { get; init; }
        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public DateTime? ReminderAtUtc { get; init; }
    }
}
