using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar.Models
{
    public sealed record CalendarOverviewDto(
    DateOnly Date,
    IReadOnlyList<TaskOverviewDto> Tasks,
    IReadOnlyList<NoteOverviewDto> Notes
    // Later: IReadOnlyList<EventOverviewDto> Events
);
}
