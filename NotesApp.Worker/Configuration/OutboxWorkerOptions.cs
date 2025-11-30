using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace NotesApp.Worker.Configuration
{
    /// <summary>
    /// Configuration options for the OutboxProcessingWorker.
    /// These values are typically bound from configuration (e.g. "OutboxWorker" section).
    /// </summary>
    public sealed class OutboxWorkerOptions
    {
        /// <summary>
        /// Maximum number of outbox messages to process in a single batch.
        /// Must be at least 1.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int MaxBatchSize { get; set; } = 50;

        /// <summary>
        /// Delay between polling cycles, in milliseconds.
        /// Must be at least 100ms to avoid busy-waiting.
        /// </summary>
        [Range(100, int.MaxValue)]
        public int PollingIntervalMilliseconds { get; set; } = 5_000;

        /// <summary>
        /// Maximum number of dispatch attempts before the message is considered "poison".
        /// This is a soft limit used for logging/alerting – processing can still continue.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int MaxRetryAttempts { get; set; } = 10;

        /// <summary>
        /// Name of the configuration section used to bind these options.
        /// Keeping it here avoids magic strings in Program.cs.
        /// </summary>
        public const string SectionName = "OutboxWorker";
    }
}
