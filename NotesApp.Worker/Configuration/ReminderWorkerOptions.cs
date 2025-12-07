using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace NotesApp.Worker.Configuration
{
    /// <summary>
    /// Configuration options for the ReminderMonitorWorker.
    /// Values are typically bound from the "ReminderWorker" configuration section.
    /// </summary>
    public sealed class ReminderWorkerOptions
    {
        /// <summary>
        /// How often the worker checks for overdue reminders.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int PollingIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// Maximum number of reminders to process in a single iteration.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int MaxRemindersPerBatch { get; set; } = 100;

        /// <summary>
        /// Name of the configuration section used to bind these options.
        /// </summary>
        public const string SectionName = "ReminderWorker";
    }
}
