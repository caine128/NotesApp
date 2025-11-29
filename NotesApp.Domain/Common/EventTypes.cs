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
}
