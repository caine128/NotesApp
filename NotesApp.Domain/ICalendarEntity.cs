using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain
{
    /// <summary>
    /// Interface for calendar-based syncable entities that are organized by date.
    /// 
    /// Extends IVersionedSyncableEntity with a Date property for:
    /// - Calendar view queries (get items for a specific day/week/month)
    /// - Date-based filtering and navigation
    /// 
    /// Implemented by:
    /// - Note - daily journal/note entries
    /// - TaskItem - scheduled tasks and reminders
    /// </summary>
    public interface ICalendarEntity : IVersionedSyncableEntity
    {
        /// <summary>
        /// The calendar date this entity belongs to.
        /// Used for organizing and querying by day.
        /// </summary>
        DateOnly Date { get; }
    }
}
