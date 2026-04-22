using System.ComponentModel.DataAnnotations;

namespace NotesApp.Application.Configuration
{
    /// <summary>
    /// Configuration options for recurring-task materialization behaviour.
    ///
    /// These values are used by:
    /// - CreateTaskCommandHandler           (initial batch on series creation)
    /// - UpdateRecurringTaskOccurrenceCommandHandler          (initial batch on ThisAndFollowing split)
    /// - UpdateRecurringTaskOccurrenceSubtasksCommandHandler  (initial batch on ThisAndFollowing split)
    ///
    /// The horizon worker (NotesApp.Worker) reads HorizonWeeksAhead from the same section
    /// to keep both layers in sync.
    ///
    /// Bind from configuration section "RecurringTask" in appsettings.json:
    /// {
    ///   "RecurringTask": {
    ///     "HorizonWeeksAhead": 8,
    ///     "InitialMaterializationBatchSize": 26
    ///   }
    /// }
    /// </summary>
    public sealed class RecurringTaskOptions
    {
        public const string SectionName = "RecurringTask";

        /// <summary>
        /// How many weeks ahead of today to keep materialized occurrences.
        /// Handlers use this to decide whether a new series segment falls inside the
        /// active horizon (materialize now) or beyond it (leave to the worker).
        /// Default: 8 weeks.
        /// </summary>
        [Range(1, 52)]
        public int HorizonWeeksAhead { get; set; } = 8;

        /// <summary>
        /// Maximum number of TaskItems to materialize per series in the initial batch
        /// created when a recurring task or series segment is first added.
        /// The horizon worker advances materialization further on its next poll.
        /// Default: 26 (approximately 6 months of weekly occurrences).
        /// </summary>
        [Range(1, int.MaxValue)]
        public int InitialMaterializationBatchSize { get; set; } = 26;
    }
}
