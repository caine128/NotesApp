using NotesApp.Application.Notes.Models;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes
{


    public static class NoteMappings
    {
        public static NoteDetailDto ToDetailDto(this Note note) =>
          new(note.Id,
              note.Title,
              note.Date,
              note.Summary,
              note.Tags,
              note.CreatedAtUtc,
              note.UpdatedAtUtc);

        public static NoteSummaryDto ToSummaryDto(this Note note) =>
            new(
                note.Id,
                note.Title,
                note.Date
            );

        public static NoteOverviewDto ToOverviewDto(this Note note) =>
            new(
                note.Title,
                note.Date
            );


        public static IReadOnlyList<NoteSummaryDto> ToSummaryDtoList(this IEnumerable<Note> notes) =>
            notes.Select(n => n.ToSummaryDto()).ToList();

        public static IReadOnlyList<NoteOverviewDto> ToOverviewDtoList(this IEnumerable<Note> notes) =>
            notes.Select(n => n.ToOverviewDto()).ToList();
    }
}
