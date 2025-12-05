using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Common
{
    /// <summary>
    /// Centralized builder for JSON payloads stored in OutboxMessage.Payload.
    /// 
    /// This keeps the payload schema consistent across all producers
    /// (sync push, conflict resolution, reminder acknowledgment, etc.).
    /// </summary>
    public static class OutboxPayloadBuilder
    {
        /// <summary>
        /// Builds the standardized JSON payload for task-related outbox messages.
        /// </summary>
        public static string BuildTaskPayload(TaskItem task, Guid originDeviceId)
        {
            var payload = new
            {
                TaskId = task.Id,
                task.UserId,
                task.Date,
                task.Title,
                task.IsCompleted,
                task.ReminderAtUtc,
                task.Version,
                OriginDeviceId = originDeviceId
            };

            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// Builds the standardized JSON payload for note-related outbox messages.
        /// </summary>
        public static string BuildNotePayload(Note note, Guid originDeviceId)
        {
            var payload = new
            {
                NoteId = note.Id,
                note.UserId,
                note.Date,
                note.Title,
                note.Version,
                OriginDeviceId = originDeviceId
            };

            return JsonSerializer.Serialize(payload);
        }
    }
}
