using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks
{
    /// <summary>
    /// Read model for the "year overview" screen:
    /// aggregates tasks per month for a single user.
    /// </summary>
    public sealed record MonthTasksOverviewDto
    {
        /// <summary>
        /// The year that this overview entry refers to.
        /// </summary>
        public int Year { get; init; }

        /// <summary>
        /// The month number (1-12).
        /// </summary>
        public int Month { get; init; }

        /// <summary>
        /// Total number of tasks in this month (excluding soft-deleted ones).
        /// </summary>
        public int TotalTasks { get; init; }

        /// <summary>
        /// Number of tasks that are completed in this month.
        /// </summary>
        public int CompletedTasks { get; init; }

        /// <summary>
        /// Number of tasks that are still pending in this month.
        /// This is a convenience property (TotalTasks - CompletedTasks).
        /// </summary>
        public int PendingTasks { get; init; }
    }
}
