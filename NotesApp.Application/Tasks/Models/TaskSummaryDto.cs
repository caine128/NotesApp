using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks.Models
{
    public sealed record TaskSummaryDto(Guid TaskId,
                                        string Title,
                                        DateOnly Date,
                                        TimeOnly? StartTime,
                                        TimeOnly? EndTime,
                                        bool IsCompleted,
                                        string? Location,
                                        TimeSpan? TravelTime);
}
