using FluentResults;
using MediatR;
using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrenceSubtasks
{
    /// <summary>
    /// Replaces the complete subtask list for a recurring task occurrence according to the specified scope.
    ///
    /// The client always sends the desired final state of the subtask list (full replace, not a patch).
    /// Works for both materialized and virtual occurrences — no separate command needed.
    ///
    /// Scope semantics:
    /// - <see cref="RecurringEditScope.Single"/>:
    ///     Materialized occurrence (<see cref="TaskItemId"/> provided):
    ///       Replaces the <see cref="Domain.Entities.Subtask"/> rows directly on the TaskItem.
    ///       No exception is created — the TaskItem itself is the concrete record.
    ///     Virtual occurrence (<see cref="TaskItemId"/> null):
    ///       Creates or updates a <see cref="Domain.Entities.RecurringTaskException"/> and stores the
    ///       desired list as <see cref="Domain.Entities.RecurringTaskSubtask"/> rows with ExceptionId set.
    /// - <see cref="RecurringEditScope.ThisAndFollowing"/>:
    ///     Terminates the current series at <see cref="OccurrenceDate"/>, soft-deletes the old
    ///     template subtasks, and creates a new series segment (inheriting all other template fields)
    ///     with the new subtask list as its template. Materialized TaskItems from
    ///     <see cref="OccurrenceDate"/> forward are soft-deleted and re-materialized with the new
    ///     template subtasks. Pre-split virtual occurrences continue to use the old template.
    /// - <see cref="RecurringEditScope.All"/>:
    ///     Replaces template subtasks on every active series segment for the recurring root.
    ///     Also replaces subtask rows on all already-materialized TaskItems that have no individual
    ///     exception (individually-modified occurrences are preserved).
    /// </summary>
    public sealed class UpdateRecurringTaskOccurrenceSubtasksCommand : IRequest<Result>
    {
        /// <summary>How many occurrences to update.</summary>
        public RecurringEditScope Scope { get; init; } = RecurringEditScope.Single;

        /// <summary>
        /// Id of the materialized TaskItem to update.
        /// Provide for <see cref="RecurringEditScope.Single"/> when the occurrence is materialized.
        /// Null for virtual occurrences (Single scope) or bulk scopes (ThisAndFollowing / All).
        /// </summary>
        public Guid? TaskItemId { get; init; }

        /// <summary>
        /// The series that owns this occurrence.
        /// Required for all scopes.
        /// </summary>
        public Guid SeriesId { get; init; }

        /// <summary>
        /// The canonical (recurrence-engine-generated) occurrence date.
        /// Required for <see cref="RecurringEditScope.Single"/> (virtual) and
        /// <see cref="RecurringEditScope.ThisAndFollowing"/>. Optional for All scope.
        /// </summary>
        public DateOnly OccurrenceDate { get; init; }

        /// <summary>
        /// The desired complete subtask list.
        /// Sending an empty list clears all subtasks:
        ///   - Single materialized: removes all Subtask rows from the TaskItem.
        ///   - Single virtual: explicitly sets zero subtasks for this occurrence
        ///     (creates a RecurringTaskException with no subtask rows so the occurrence
        ///      is NOT reverted to the series template on next render).
        ///   - ThisAndFollowing / All: clears the template (and resets materialized subtasks).
        /// </summary>
        public IReadOnlyList<RecurringOccurrenceSubtaskDto> Subtasks { get; init; } = [];
    }

    /// <summary>One subtask in the desired list for a recurring occurrence update.</summary>
    public sealed record RecurringOccurrenceSubtaskDto(
        string Text,
        string Position,
        bool IsCompleted);
}
