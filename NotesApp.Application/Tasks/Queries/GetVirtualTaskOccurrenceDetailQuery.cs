using FluentResults;
using MediatR;
using NotesApp.Application.Tasks.Models;
using System;

namespace NotesApp.Application.Tasks.Queries
{
    /// <summary>
    /// Returns detail for a virtual (non-materialized) recurring task occurrence.
    ///
    /// A virtual occurrence has no TaskItem row — it is projected on the fly from the
    /// <see cref="Domain.Entities.RecurringTaskSeries"/> template, with any
    /// <see cref="Domain.Entities.RecurringTaskException"/> overrides applied on top.
    ///
    /// Use this query when the client taps on an occurrence that has
    /// <c>IsVirtualOccurrence = true</c> in the summary list.
    /// For materialized occurrences (IsVirtualOccurrence = false), use <c>GetTaskDetailQuery</c>.
    /// </summary>
    public sealed class GetVirtualTaskOccurrenceDetailQuery : IRequest<Result<TaskDetailDto>>
    {
        /// <summary>
        /// The series that owns this virtual occurrence.
        /// </summary>
        public Guid SeriesId { get; init; }

        /// <summary>
        /// The canonical (recurrence-engine-generated) date for this occurrence.
        /// </summary>
        public DateOnly OccurrenceDate { get; init; }
    }
}
