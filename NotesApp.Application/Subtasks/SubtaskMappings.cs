using NotesApp.Application.Subtasks.Models;
using NotesApp.Domain.Entities;

namespace NotesApp.Application.Subtasks
{
    /// <summary>
    /// Extension methods for mapping Subtask domain entities to DTOs.
    /// </summary>
    public static class SubtaskMappings
    {
        /// <summary>
        /// Maps a <see cref="Subtask"/> domain entity to a <see cref="SubtaskDto"/>.
        /// Used by REST API responses and <see cref="NotesApp.Application.Tasks.Queries.GetTaskDetailQueryHandler"/>.
        /// </summary>
        public static SubtaskDto ToSubtaskDto(this Subtask subtask) =>
            new(subtask.Id,
                subtask.Text,
                subtask.IsCompleted,
                subtask.Position,
                subtask.Version,
                subtask.CreatedAtUtc,
                subtask.UpdatedAtUtc);
    }
}
