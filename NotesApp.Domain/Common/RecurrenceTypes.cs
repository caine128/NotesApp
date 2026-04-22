using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Frequency at which a recurring task series repeats.
    /// Maps to RFC 5545 FREQ values supported by the recurrence engine.
    /// </summary>
    public enum RecurrenceFrequency
    {
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    /// <summary>
    /// Determines when a recurring task series ends.
    /// </summary>
    public enum RecurrenceEndCondition
    {
        /// <summary>Series continues indefinitely (materialized up to the rolling horizon).</summary>
        Never,

        /// <summary>Series ends on a specific date (stored in EndsBeforeDate, exclusive).</summary>
        OnDate,

        /// <summary>Series ends after a fixed number of occurrences (encoded as COUNT in RRuleString).</summary>
        AfterCount
    }

    /// <summary>
    /// Scope of an edit operation applied to a recurring task occurrence.
    /// </summary>
    public enum RecurringEditScope
    {
        /// <summary>Modify only this single occurrence.</summary>
        Single,

        /// <summary>Modify this occurrence and all future occurrences in the same series.</summary>
        ThisAndFollowing,

        /// <summary>Modify all occurrences across every series segment sharing the same root.</summary>
        All
    }

    /// <summary>
    /// Scope of a delete operation applied to a recurring task occurrence.
    /// </summary>
    public enum RecurringDeleteScope
    {
        /// <summary>Delete only this single occurrence.</summary>
        Single,

        /// <summary>Delete this occurrence and all future occurrences in the same series.</summary>
        ThisAndFollowing,

        /// <summary>Delete all occurrences across every series segment sharing the same root.</summary>
        All
    }
}
