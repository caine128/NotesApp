using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks
{
    public sealed record TaskDto
    {
        public Guid TaskId { get; init; }
        public Guid UserId { get; init; }
        public DateOnly Date { get; init; }
        public string Title { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public DateTime? ReminderAtUtc { get; init; }
    }
}
