using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    public sealed record TaskDetailDto(Guid TaskId,
                                       string Title,
                                       string? Description,
                                       DateOnly Date,
                                       TimeOnly? StartTime,
                                       TimeOnly? EndTime,
                                       bool IsCompleted,
                                       string? Location,
                                       TimeSpan? TravelTime,
                                       DateTime CreatedAtUtc,
                                       DateTime UpdatedAtUtc,
                                       DateTime? ReminderAtUtc);
}
