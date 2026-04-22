using FluentResults;
using MediatR;
using NotesApp.Domain.Common;
using System;

namespace NotesApp.Application.Tasks.Commands.DeleteRecurringTaskOccurrence
{
    /// <summary>
    /// Deletes one or more occurrences of a recurring task according to the specified scope.
    ///
    /// Scope semantics:
    /// - <see cref="RecurringDeleteScope.Single"/>:
    ///     If <see cref="TaskItemId"/> is provided → soft-delete the materialized TaskItem and
    ///     create a deletion <see cref="Domain.Entities.RecurringTaskException"/> as a permanent tombstone.
    ///     If <see cref="TaskItemId"/> is null → virtual occurrence; create a deletion exception only.
    /// - <see cref="RecurringDeleteScope.ThisAndFollowing"/>:
    ///     Terminate the series at <see cref="OccurrenceDate"/>, soft-delete all materialized
    ///     TaskItems from that date forward, and soft-delete all exceptions from that date forward.
    /// - <see cref="RecurringDeleteScope.All"/>:
    ///     Soft-delete the root, all series segments, all materialized TaskItems, and all exceptions.
    ///
    /// The handler always uses the change-tracker pattern for bulk soft-deletes, so every operation
    /// within a single handler call commits in exactly one SaveChangesAsync() call.
    /// </summary>
    public sealed class DeleteRecurringTaskOccurrenceCommand : IRequest<Result>
    {
        /// <summary>
        /// Id of the materialized TaskItem to delete.
        /// Required for <see cref="RecurringDeleteScope.Single"/> when the occurrence is materialized.
        /// Null for virtual occurrences (Single scope) or bulk scopes (ThisAndFollowing / All).
        /// The API controller sets this from the route parameter — settable so the controller can override.
        /// </summary>
        public Guid? TaskItemId { get; set; }

        /// <summary>
        /// Id of the RecurringTaskSeries that owns the occurrence being deleted.
        /// Required for all scopes.
        /// Used to:
        ///   - Locate the series for termination (ThisAndFollowing).
        ///   - Resolve the RootId for bulk operations (All).
        ///   - Key the deletion exception (Single).
        /// </summary>
        public Guid SeriesId { get; init; }

        /// <summary>
        /// Canonical (recurrence-engine-generated) occurrence date.
        /// Required for <see cref="RecurringDeleteScope.Single"/> and
        /// <see cref="RecurringDeleteScope.ThisAndFollowing"/>.
        /// </summary>
        public DateOnly OccurrenceDate { get; init; }

        /// <summary>
        /// How many occurrences to delete.
        /// </summary>
        public RecurringDeleteScope Scope { get; init; }
    }
}
