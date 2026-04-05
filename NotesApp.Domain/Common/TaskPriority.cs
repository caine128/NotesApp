namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Priority level for a task.
    /// The field is always set — tasks without an explicit priority get <see cref="Normal"/>.
    /// Values start at 1; 0 is intentionally unmapped so that a forgotten default never silently
    /// produces a valid priority.
    /// </summary>
    public enum TaskPriority
    {
        /// <summary>Low-importance task.</summary>
        Low = 1,

        /// <summary>Standard priority. Default for all tasks.</summary>
        Normal = 2,

        /// <summary>High-importance task that should be attended to first.</summary>
        High = 3,
    }
}
