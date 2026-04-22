using System.ComponentModel.DataAnnotations;

namespace NotesApp.Worker.Configuration
{
    /// <summary>
    /// Worker-specific configuration for the <see cref="NotesApp.Worker.RecurringTaskHorizonWorker"/>.
    /// Bound from the "Worker:RecurringTaskHorizon" configuration section.
    ///
    /// Horizon and batch-size settings shared with the Application layer live in
    /// <c>RecurringTaskOptions</c> (NotesApp.Application) under the "RecurringTask" section.
    /// </summary>
    public sealed class RecurringTaskHorizonWorkerOptions
    {
        public const string SectionName = "Worker:RecurringTaskHorizon";

        /// <summary>
        /// How often the worker polls for series that need materialization (seconds).
        /// Default: 3600 (1 hour).
        /// </summary>
        [Range(1, int.MaxValue)]
        public int PollingIntervalSeconds { get; set; } = 3600;

        /// <summary>
        /// Maximum number of series to process in a single worker loop iteration.
        /// Each series is committed in its own SaveChangesAsync() call.
        /// Default: 50.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int MaxSeriesPerBatch { get; set; } = 50;
    }
}
