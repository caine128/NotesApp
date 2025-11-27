using NotesApp.Application.Calendar.Models;
using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks;
using NotesApp.Application.Notes;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Calendar
{
    public static class CalendarMappings
    {
        public static CalendarSummaryDto ToCalendarSummaryDto(DateOnly date,
                                                              IEnumerable<TaskItem> tasks,
                                                              IEnumerable<Note> notes)
        {
            return ToCalendarSummaryDtoList(date, date.AddDays(1), tasks, notes)[0];
        }

        public static IReadOnlyList<CalendarSummaryDto> ToCalendarSummaryDtoList(
            DateOnly start,
            DateOnly endExclusive,
            IEnumerable<TaskItem> tasks,
            IEnumerable<Note> notes)
        {
            var tasksByDate = tasks
                .GroupBy(t => t.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(t => t.StartTime)
                          .ThenBy(t => t.Title)
                          .ToSummaryDtoList());

            var notesByDate = notes
                .GroupBy(n => n.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(n => n.Title)
                          .ToSummaryDtoList());

            var result = new List<CalendarSummaryDto>();

            for (var date = start; date < endExclusive; date = date.AddDays(1))
            {
                tasksByDate.TryGetValue(date, out var dayTasks);
                notesByDate.TryGetValue(date, out var dayNotes);

                result.Add(new CalendarSummaryDto(
                    date,
                    dayTasks ?? Array.Empty<TaskSummaryDto>(),
                    dayNotes ?? Array.Empty<NoteSummaryDto>()));
            }

            return result;
        }

        public static IReadOnlyList<CalendarOverviewDto> ToCalendarOverviewDtoList(
            DateOnly start,
            DateOnly endExclusive,
            IEnumerable<TaskItem> tasks,
            IEnumerable<Note> notes)
        {
            var tasksByDate = tasks
                .GroupBy(t => t.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(t => t.Date)
                          .ToOverviewDtoList());

            var notesByDate = notes
                .GroupBy(n => n.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(n => n.Date)
                          .ToOverviewDtoList());

            var result = new List<CalendarOverviewDto>();

            for (var date = start; date < endExclusive; date = date.AddDays(1))
            {
                tasksByDate.TryGetValue(date, out var dayTasks);
                notesByDate.TryGetValue(date, out var dayNotes);

                result.Add(new CalendarOverviewDto(
                    date,
                    dayTasks ?? Array.Empty<TaskOverviewDto>(),
                    dayNotes ?? Array.Empty<NoteOverviewDto>()));
            }

            return result;
        }
    }
}
