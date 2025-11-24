using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Notes
{
    /// <summary>
    /// Lightweight DTO returned by the API for notes.
    /// 
    /// This is what the React / React Native clients will see.
    /// It intentionally mirrors the most important fields from the Note entity,
    /// but without exposing any domain behavior or EF-specific details.
    /// </summary>
    public sealed class NoteDto
    {
        public Guid NoteId { get; init; }
        public Guid UserId { get; init; }
        public DateOnly Date { get; init; }

        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;

        /// <summary>
        /// Optional AI- or user-generated summary.
        /// Not used in v1, but we include it so the API is future-ready.
        /// </summary>
        public string? Summary { get; init; }

        /// <summary>
        /// Optional tags, e.g. comma-separated or JSON, depending on future needs.
        /// Again, present for AI & search features later.
        /// </summary>
        public string? Tags { get; init; }
    }
}
