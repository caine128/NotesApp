using NotesApp.Application.Subtasks.Models;
using System;
using System.Collections.Generic;

namespace NotesApp.Application.Tasks.Models
{
    /// <summary>
    /// Recurrence rule supplied when creating a recurring task series via
    /// <see cref="NotesApp.Application.Tasks.Commands.CreateTask.CreateTaskCommand"/>.
    /// Contains all the parameters needed to create a RecurringTaskRoot + RecurringTaskSeries.
    /// </summary>
    public sealed record RecurrenceRuleDto(
        /// <summary>
        /// RFC 5545 RRULE body without DTSTART or UNTIL, e.g. "FREQ=WEEKLY;BYDAY=MO,WE".
        /// COUNT may be present for the AfterCount end condition.
        /// </summary>
        string RRuleString,

        /// <summary>
        /// Inclusive start date for the first series segment.
        /// Typically equals the task's Date, but the client may supply a future start.
        /// </summary>
        DateOnly StartsOnDate,

        /// <summary>
        /// Exclusive end date for the series. Null for Never or AfterCount end conditions.
        /// </summary>
        DateOnly? EndsBeforeDate,

        /// <summary>
        /// Reminder offset in minutes before StartTime. Null = no reminder.
        /// Stored as an offset rather than absolute UTC because each occurrence has a different date.
        /// </summary>
        int? ReminderOffsetMinutes,

        /// <summary>
        /// Optional list of subtask templates to create with each occurrence.
        /// </summary>
        IReadOnlyList<TemplateSubtaskDto>? TemplateSubtasks);
}
