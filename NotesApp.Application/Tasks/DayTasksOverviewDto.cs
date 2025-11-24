using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tasks
{
    public sealed record DayTasksOverviewDto
    {
        /// <summary>
        /// The calendar date this overview entry refers to.
        /// </summary>
        public DateOnly Date { get; init; }

        /// <summary>
        /// Total number of tasks for this user on this date (excluding soft-deleted ones).
        /// </summary>
        public int TotalTasks { get; init; }

        /// <summary>
        /// How many of those tasks are marked as completed.
        /// </summary>
        public int CompletedTasks { get; init; }

        /// <summary>
        /// True if at least one task on this date has a reminder set.
        /// This is handy if later we want to show a different indicator for “reminder days”.
        /// </summary>
        public bool HasAnyReminder { get; init; }
    }
}
