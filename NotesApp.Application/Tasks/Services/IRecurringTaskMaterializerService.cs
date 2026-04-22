using FluentResults;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Tasks.Services
{
    /// <summary>
    /// Pure application-layer service that projects a <see cref="RecurringTaskSeries"/> into
    /// concrete <see cref="TaskItem"/> and <see cref="Subtask"/> instances ready for persistence.
    ///
    /// This service performs no EF / IO operations — it only computes which occurrences to create
    /// and returns them as in-memory objects. The caller is responsible for:
    /// 1. Calling <see cref="IRecurringTaskSubtaskRepository.GetBySeriesIdAsync"/> (template subtasks)
    ///    and <see cref="IRecurringTaskSubtaskRepository.GetByExceptionIdsAsync"/> (exception subtask
    ///    overrides) before invoking the materializer.
    /// 2. Persisting all returned objects via repository AddAsync calls.
    /// 3. Committing atomically with a single SaveChangesAsync().
    ///
    /// Recurrence date generation is delegated to <see cref="IRecurrenceEngine"/> (injected).
    /// </summary>
    public interface IRecurringTaskMaterializerService
    {
        /// <summary>
        /// Materializes the first batch of occurrences for a newly created recurring series.
        /// Used by <c>CreateTaskCommandHandler</c> immediately after the series is created.
        ///
        /// Generates dates from <c>series.StartsOnDate</c> up to
        /// <c>today + HorizonWeeksAhead</c> (or the RRULE end), creates one <see cref="TaskItem"/>
        /// per occurrence (applying exception overrides where present), and copies subtasks from
        /// either the exception subtask list (if non-empty for this occurrence) or the template list.
        /// </summary>
        /// <param name="series">The newly created series.</param>
        /// <param name="templateSubtasks">Template subtasks for the series (from GetBySeriesIdAsync).</param>
        /// <param name="exceptions">Any pre-existing exceptions in the materialization range.</param>
        /// <param name="exceptionSubtasksById">
        /// Exception subtask overrides keyed by ExceptionId (from GetByExceptionIdsAsync).
        /// </param>
        /// <param name="utcNow">Current UTC time (used for audit fields on created entities).</param>
        /// <param name="batchSize">Maximum number of TaskItems to create.</param>
        /// <returns>
        /// A <see cref="Result{T}"/> containing the batch on success, or a failure with descriptive
        /// errors if any occurrence or subtask factory method returns a domain validation error.
        /// </returns>
        Result<MaterializationBatch> MaterializeInitialBatch(RecurringTaskSeries series,
                                                             IReadOnlyList<RecurringTaskSubtask> templateSubtasks,
                                                             IReadOnlyList<RecurringTaskException> exceptions,
                                                             IReadOnlyDictionary<Guid, IReadOnlyList<RecurringTaskSubtask>> exceptionSubtasksById,
                                                             DateTime utcNow,
                                                             int batchSize);

        /// <summary>
        /// Advances the materialization horizon for an existing series.
        /// Used by <c>RecurringTaskHorizonWorker</c> to fill in future occurrences on each poll.
        ///
        /// Generates dates in the range
        /// <c>(series.MaterializedUpToDate, targetDate]</c>, skipping any occurrences that
        /// have already been materialized (covered by exceptions with MaterializedTaskItemId set)
        /// or deleted (IsDeletion exceptions).
        /// </summary>
        /// <param name="series">The series to advance.</param>
        /// <param name="templateSubtasks">Template subtasks for the series.</param>
        /// <param name="exceptions">Exceptions in the range (MaterializedUpToDate, targetDate].</param>
        /// <param name="exceptionSubtasksById">Exception subtask overrides keyed by ExceptionId.</param>
        /// <param name="targetDate">The new materialization horizon (inclusive).</param>
        /// <param name="utcNow">Current UTC time.</param>
        /// <returns>
        /// A <see cref="Result{T}"/> containing the batch on success, or a failure with descriptive
        /// errors if any occurrence or subtask factory method returns a domain validation error.
        /// </returns>
        Result<MaterializationBatch> AdvanceHorizon(RecurringTaskSeries series,
                                                    IReadOnlyList<RecurringTaskSubtask> templateSubtasks,
                                                    IReadOnlyList<RecurringTaskException> exceptions,
                                                    IReadOnlyDictionary<Guid, IReadOnlyList<RecurringTaskSubtask>> exceptionSubtasksById,
                                                    DateOnly targetDate,
                                                    DateTime utcNow);
    }

    /// <summary>
    /// The result of a materialization operation.
    /// Both lists are ready to be passed to repository AddAsync calls before SaveChangesAsync().
    /// </summary>
    public sealed record MaterializationBatch(
        IReadOnlyList<TaskItem> Tasks,
        IReadOnlyList<Subtask> Subtasks);
}
