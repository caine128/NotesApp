using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync
{
    public static class SyncMappings
    {
        public static TaskSyncItemDto ToSyncDto(this TaskItem task)
        {
            return new TaskSyncItemDto
            {
                Id = task.Id,
                Date = task.Date,
                Title = task.Title,
                IsCompleted = task.IsCompleted,
                Description = task.Description,
                StartTime = task.StartTime,
                EndTime = task.EndTime,
                Location = task.Location,
                TravelTime = task.TravelTime,
                ReminderAtUtc = task.ReminderAtUtc,
                Version = task.Version,
                CreatedAtUtc = task.CreatedAtUtc,
                UpdatedAtUtc = task.UpdatedAtUtc
            };
        }

        public static NoteSyncItemDto ToSyncDto(this Note note)
        {
            return new NoteSyncItemDto
            {
                Id = note.Id,
                Date = note.Date,
                Title = note.Title,
                Content = note.Content,
                Summary = note.Summary,
                Tags = note.Tags,
                Version = note.Version,
                CreatedAtUtc = note.CreatedAtUtc,
                UpdatedAtUtc = note.UpdatedAtUtc
            };
        }

        public static IReadOnlyList<TaskSyncItemDto> ToSyncTaskDtos(this IEnumerable<TaskItem> tasks)
            => tasks.Select(t => t.ToSyncDto()).ToList();

        public static IReadOnlyList<NoteSyncItemDto> ToSyncNoteDtos(this IEnumerable<Note> notes)
            => notes.Select(n => n.ToSyncDto()).ToList();
    }
}
