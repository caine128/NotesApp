using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes
{
    /// <summary>
    /// Mapping helpers between the Note domain entity and NoteDto.
    /// 
    /// Keeping mappings in a dedicated class makes handlers thin and
    /// makes it easy to evolve the DTO later.
    /// </summary>
    public static class NoteMappings
    {
        public static NoteDto ToDto(this Note note)
            => new()
            {
                NoteId = note.Id,
                UserId = note.UserId,
                Date = note.Date,
                Title = note.Title,
                Content = note.Content,
                Summary = note.Summary,
                Tags = note.Tags
            };

        public static IReadOnlyList<NoteDto> ToDtoList(this IEnumerable<Note> notes)
            => notes
                .Select(n => n.ToDto())
                .ToList();
    }
}
