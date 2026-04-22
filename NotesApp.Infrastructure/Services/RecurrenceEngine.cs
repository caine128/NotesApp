using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using NotesApp.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotesApp.Infrastructure.Services
{
    /// <summary>
    /// Ical.Net 5.x-backed implementation of <see cref="IRecurrenceEngine"/>.
    /// Wraps RFC 5545 recurrence evaluation; all edge cases (leap years, month-end clamping,
    /// BYDAY, BYSETPOS, COUNT) are delegated to the Ical.Net library.
    ///
    /// Ical.Net 5.x API notes:
    /// - <c>GetOccurrences(CalDateTime start, EvaluationOptions? options)</c> takes no end-date
    ///   parameter. The upper boundary is applied via <c>TakeWhileBefore(periodEnd)</c> (added in
    ///   5.1.0), which is the library-idiomatic replacement for the old end-date overload.
    /// - <c>EvaluationOptions.MaxUnmatchedIncrementsLimit</c> is a safety guard against pathological
    ///   RRULE strings that produce very sparse matches (e.g. "5th Monday of the month").
    ///   It counts evaluator steps without a hit; normal rules never approach the limit.
    ///   When exceeded, <c>EvaluationLimitExceededException</c> is thrown — this propagates to
    ///   the caller's exception handler (horizon worker try/catch or command handler).
    /// - <c>CalDateTime(DateOnly date)</c> — direct DateOnly constructor (no y/m/d decomposition).
    /// - <c>occurrence.Period.StartTime.Date</c> — returns <c>DateOnly</c> directly in 5.x.
    /// </summary>
    internal sealed class RecurrenceEngine : IRecurrenceEngine
    {
        // Safety guard: limits unmatched evaluator increments before an occurrence is found.
        // Prevents an indefinite loop for sparse or pathological RRULE strings.
        // 1000 is very generous; normal rules (daily/weekly/monthly) will never approach it.
        private const int MaxUnmatchedIncrements = 1000;

        /// <inheritdoc />
        public IEnumerable<DateOnly> GenerateOccurrences(string rruleString,
                                                         DateOnly dtStart,
                                                         DateOnly? endsBeforeDate,
                                                         DateOnly fromInclusive,
                                                         DateOnly toExclusive)
        {
            // DTSTART is set on the CalendarEvent; CalDateTime accepts DateOnly directly in 5.x.
            var dtStartCal = new CalDateTime(dtStart);

            var vEvent = new CalendarEvent
            {
                DtStart = dtStartCal,
                RecurrenceRules = { new RecurrencePattern(rruleString) }
            };

            var calendar = new Calendar();
            calendar.Events.Add(vEvent);

            // Collapse the two exclusive upper-bound conditions into one:
            //   toExclusive  — the query window end
            //   endsBeforeDate — series split / user-defined end cap
            // Whichever is earlier wins.
            var effectiveEnd = (endsBeforeDate.HasValue && endsBeforeDate.Value < toExclusive)
                ? endsBeforeDate.Value
                : toExclusive;

            var windowStart = new CalDateTime(fromInclusive);
            var periodEnd   = new CalDateTime(effectiveEnd);

            var options = new EvaluationOptions
            {
                MaxUnmatchedIncrementsLimit = MaxUnmatchedIncrements
            };

            return vEvent
                .GetOccurrences(windowStart, options)
                .TakeWhileBefore(periodEnd)         // library-idiomatic upper-bound stop (5.1+)
                .Select(o => o.Period.StartTime.Date)
                .Where(d => d >= fromInclusive);    // belt-and-suspenders: guard against any
                                                    // occurrence Ical.Net yields before windowStart
        }
    }
}
