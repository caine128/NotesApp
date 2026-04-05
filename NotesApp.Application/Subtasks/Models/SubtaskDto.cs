using System;

namespace NotesApp.Application.Subtasks.Models
{
    /// <summary>
    /// Represents a single subtask within a task.
    /// Returned from REST API operations (create, update) and embedded in <see cref="NotesApp.Application.Tasks.Models.TaskDetailDto"/>.
    /// </summary>
    public sealed record SubtaskDto(
        Guid SubtaskId,
        string Text,
        bool IsCompleted,
        string Position,
        long Version,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
