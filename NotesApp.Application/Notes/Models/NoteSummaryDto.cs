using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Models
{
    public sealed record NoteSummaryDto(Guid NoteId,
                                        string Title,
                                        DateOnly Date
                                        // add Time later if needed
);

}
