using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes.Models
{
    /// <summary>
    /// DTO for Note details returned from queries and commands.
    /// 
    /// CHANGED: Content property removed. Note content is now stored in blocks.
    /// To get the full note content, query the Blocks API with parentId = noteId.
    /// </summary>
    public sealed record NoteDetailDto(Guid NoteId,
                                       string Title,
                                       DateOnly Date,
                                       string? Summary,
                                       string? Tags,
                                       DateTime CreatedAtUtc,
                                       DateTime UpdatedAtUtc);

}
