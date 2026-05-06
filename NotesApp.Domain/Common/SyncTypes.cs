namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Identifies which entity family a SyncChange row pertains to.
    /// Stored as a byte in the database via EF Core conversion.
    ///
    /// APPEND-ONLY: never reorder or reuse numeric values. Adding a new family is safe; renaming or
    /// removing a value would invalidate stored payloads on existing devices.
    /// </summary>
    public enum SyncEntityFamily : byte
    {
        Task = 1,
        Note = 2,
        Block = 3,
        Asset = 4,
        Category = 5,
        Subtask = 6,
        Attachment = 7,
        RecurringTaskRoot = 8,
        RecurringTaskSeries = 9,
        RecurringTaskSubtask = 10,
        RecurringTaskException = 11,
        RecurringTaskAttachment = 12
    }

    /// <summary>
    /// The kind of mutation a SyncChange row represents.
    /// Stored as a byte in the database via EF Core conversion.
    /// </summary>
    public enum SyncOperation : byte
    {
        Created = 1,
        Updated = 2,
        Deleted = 3
    }
}
