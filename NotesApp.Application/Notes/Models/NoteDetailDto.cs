using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Models
{
    public sealed record NoteDetailDto(Guid NoteId,
                                       string Title,
                                       string Content,
                                       DateOnly Date,
                                       string? Summary,
                                       string? Tags,
                                       DateTime CreatedAtUtc,
                                       DateTime UpdatedAtUtc);

}
