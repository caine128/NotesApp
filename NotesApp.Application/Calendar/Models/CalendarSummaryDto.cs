using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Models
{
    public sealed record CalendarSummaryDto(
    DateOnly Date,
    IReadOnlyList<TaskSummaryDto> Tasks,
    IReadOnlyList<NoteSummaryDto> Notes
    // Later: IReadOnlyList<EventSummaryDto> Events
);
}
