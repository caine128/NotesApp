using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    public enum NoteEventType
    {
        Created,
        Updated,
        Deleted,
        EmbeddingRequested
    }

    public enum TaskEventType
    {
        Created,
        Updated,
        Deleted,
        CompletionChanged
    }

    /// <summary>
    /// Event types for Block entity (used in outbox messages).
    /// </summary>
    public enum BlockEventType
    {
        Created,
        Updated,
        Deleted
    }

    /// <summary>
    /// Event types for Asset entity (used in outbox messages).
    /// </summary>
    public enum AssetEventType
    {
        Created,
        Deleted
    }
}
