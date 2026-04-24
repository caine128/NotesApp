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

    /// <summary>
    /// Event types for Attachment entity (used in outbox messages).
    /// No Updated value — attachments are immutable after creation.
    /// </summary>
    // REFACTORED: added AttachmentEventType for task-attachments feature
    public enum AttachmentEventType
    {
        Created,
        Deleted
    }

    // REFACTORED: added recurring-task event types for recurring-tasks feature

    /// <summary>
    /// Event types for RecurringTaskRoot entity (used in outbox messages).
    /// </summary>
    public enum RecurringRootEventType
    {
        Created,
        Deleted
    }

    /// <summary>
    /// Event types for RecurringTaskSeries entity (used in outbox messages).
    /// </summary>
    public enum RecurringSeriesEventType
    {
        Created,
        Updated,
        Terminated
    }

    /// <summary>
    /// Event types for RecurringTaskSubtask entity (used in outbox messages).
    /// Covers both series template subtasks and exception subtask overrides.
    /// </summary>
    public enum RecurringSeriesSubtaskEventType
    {
        Created,
        Updated,
        Deleted
    }

    /// <summary>
    /// Event types for RecurringTaskException entity (used in outbox messages).
    /// </summary>
    public enum RecurringExceptionEventType
    {
        Created,
        Updated,
        Deleted
    }

    /// <summary>
    /// Event types for RecurringTaskAttachment entity (used in outbox messages).
    /// No Updated value — recurring attachments are immutable after creation.
    /// </summary>
    // REFACTORED: added RecurringAttachmentEventType for recurring-task-attachments feature
    public enum RecurringAttachmentEventType
    {
        Created,
        Deleted
    }
}
