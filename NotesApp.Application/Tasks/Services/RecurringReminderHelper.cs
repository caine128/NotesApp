using System;

namespace NotesApp.Application.Tasks.Services
{
    /// <summary>
    /// Pure static helper for computing a reminder UTC timestamp from recurring-task data.
    /// Shared by <see cref="RecurringTaskMaterializerService"/> (materialized occurrences)
    /// and <see cref="NotesApp.Application.Tasks.Queries.GetVirtualTaskOccurrenceDetailQueryHandler"/>
    /// (virtual occurrences) so the logic stays in one place.
    /// </summary>
    /// <remarks>
    /// Public so that Infrastructure layer code (e.g. <c>TaskRepository.ProjectVirtualOccurrence</c>)
    /// can call <see cref="ComputeReminderUtc"/> without duplicating the logic.
    /// The class is still stateless and has no EF or IO dependencies.
    /// </remarks>
    public static class RecurringReminderHelper
    {
        /// <summary>
        /// Resolves the reminder UTC timestamp for a single occurrence.
        ///
        /// Priority:
        /// 1. If an explicit <paramref name="overrideReminderAtUtc"/> exists (from a
        ///    RecurringTaskException), return it directly.
        /// 2. If the series has a <paramref name="reminderOffsetMinutes"/> and the occurrence
        ///    has a <paramref name="startTime"/>, compute:
        ///    <c>occurrenceDate + startTime − reminderOffsetMinutes</c>.
        ///    StartTime is treated as UTC (mobile clients supply the offset relative to local
        ///    start time, which the series template stores as an offset in minutes).
        /// 3. Otherwise, return null (no reminder for this occurrence).
        /// </summary>
        public static DateTime? ComputeReminderUtc(
            DateTime? overrideReminderAtUtc,
            int? reminderOffsetMinutes,
            DateOnly occurrenceDate,
            TimeOnly? startTime)
        {
            if (overrideReminderAtUtc.HasValue)
            {
                return overrideReminderAtUtc.Value;
            }

            if (!reminderOffsetMinutes.HasValue || !startTime.HasValue)
            {
                return null;
            }

            // Combine occurrence date + start time, then subtract the offset.
            var occurrenceStart = occurrenceDate.ToDateTime(startTime.Value, DateTimeKind.Utc);
            return occurrenceStart.AddMinutes(-reminderOffsetMinutes.Value);
        }
    }
}
