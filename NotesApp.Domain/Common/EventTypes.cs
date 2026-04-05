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

    /// <summary>
    /// Event types for TaskCategory entity (used in outbox messages).
    /// </summary>
    public enum TaskCategoryEventType
    {
        Created,
        Updated,
        Deleted
    }

    /// <summary>
    /// Event types for Subtask entity (used in outbox messages).
    /// </summary>
    // REFACTORED: added SubtaskEventType for subtasks feature
    public enum SubtaskEventType
    {
        Created,
        Updated,
        Deleted
    }
}
