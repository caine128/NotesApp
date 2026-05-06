using System;

namespace NotesApp.Worker.Configuration
{
    /// <summary>
    /// Options for <see cref="SyncChangeRetentionService"/>. Bound from the
    /// <c>SyncRetention</c> configuration section.
    /// </summary>
    public sealed class SyncRetentionOptions
    {
        public const string SectionName = "SyncRetention";

        /// <summary>How often the retention sweep runs. Default: 1 hour.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Rows newer than this age are kept regardless of ack state, for diagnostics. Default: 7 days.
        /// Set to <c>TimeSpan.Zero</c> to disable the age floor.
        /// </summary>
        public TimeSpan MinAgeForDelete { get; set; } = TimeSpan.FromDays(7);
    }
}
