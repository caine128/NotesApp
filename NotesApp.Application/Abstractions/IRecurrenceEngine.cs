using System;
using System.Collections.Generic;

namespace NotesApp.Application.Abstractions
{
    /// <summary>
    /// Generates occurrence dates for a recurring task series.
    /// Abstracts the underlying RFC 5545 / iCalendar computation library so that the
    /// Application layer has no dependency on any external package.
    /// </summary>
    public interface IRecurrenceEngine
    {
        /// <summary>
        /// Returns all occurrence dates produced by the given RRULE within the specified window.
        /// </summary>
        /// <param name="rruleString">
        /// RFC 5545 RRULE body without DTSTART or UNTIL, e.g.
        /// <c>"FREQ=WEEKLY;BYDAY=MO,WE,FR;INTERVAL=2"</c>.
        /// COUNT may be present for the AfterCount end condition.
        /// </param>
        /// <param name="dtStart">
        /// Inclusive series start date (DTSTART equivalent, stored as a separate column on
        /// <c>RecurringTaskSeries.StartsOnDate</c>).
        /// </param>
        /// <param name="endsBeforeDate">
        /// Exclusive upper bound from <c>RecurringTaskSeries.EndsBeforeDate</c>.
        /// Applied as an additional stop after the engine evaluates RRULE, so series splits and
        /// user-defined end dates are always respected regardless of what is in the RRULE string.
        /// <c>null</c> means the RRULE itself determines when to stop (Never or AfterCount via COUNT).
        /// </param>
        /// <param name="fromInclusive">Start of the query window (inclusive).</param>
        /// <param name="toExclusive">End of the query window (exclusive).</param>
        /// <returns>
        /// Occurrence dates in ascending order that fall within
        /// [<paramref name="fromInclusive"/>, <paramref name="toExclusive"/>)
        /// and before <paramref name="endsBeforeDate"/> (when set).
        /// </returns>
        IEnumerable<DateOnly> GenerateOccurrences(string rruleString,
                                                  DateOnly dtStart,
                                                  DateOnly? endsBeforeDate,
                                                  DateOnly fromInclusive,
                                                  DateOnly toExclusive);
    }
}
