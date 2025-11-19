using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks
{
    public static class TaskMappings
    {
        public static TaskDto ToDto(this TaskItem task)
        {
            return new TaskDto(id: task.Id,
                               tenantId: task.TenantId,
                               date: task.Date,
                               title: task.Title,
                               content: task.Content,
                               isCompleted: task.IsCompleted,
                               reminderAtUtc: task.ReminderAtUtc,
                               createdAtUtc: task.CreatedAtUtc,
                               updatedAtUtc: task.UpdatedAtUtc,
                               isDeleted: task.IsDeleted);
        }

        public static IReadOnlyList<TaskDto> ToDtoList(this IEnumerable<TaskItem> tasks)
        {
            // Simple helper for list mapping – keeps handlers tidy
            return tasks
                .Select(t => t.ToDto())
                .ToList();
        }
    }
}
