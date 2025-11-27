using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Models
{
    public sealed record NoteOverviewDto(string Title,
                                         DateOnly Date);

}
