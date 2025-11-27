using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
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
    public sealed class CreateTaskCommand : IRequest<Result<TaskDetailDto>>
    {
        public DateOnly Date { get; init; }

        /// <summary>
        /// Required title for the task.
        /// </summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Optional description/details.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Optional local start time.
        /// </summary>
        public TimeOnly? StartTime { get; init; }

        /// <summary>
        /// Optional local end time.
        /// </summary>
        public TimeOnly? EndTime { get; init; }

        /// <summary>
        /// Optional location (address / client name etc.).
        /// </summary>
        public string? Location { get; init; }

        /// <summary>
        /// Optional travel time needed to reach the location.
        /// </summary>
        public TimeSpan? TravelTime { get; init; }

        /// <summary>
        /// Optional reminder time in UTC.
        /// </summary>
        public DateTime? ReminderAtUtc { get; init; }
    }
}
