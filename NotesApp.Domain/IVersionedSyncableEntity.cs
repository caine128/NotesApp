using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain
{
    /// <summary>
    /// Interface for syncable entities that support optimistic concurrency via version numbers.
    /// 
    /// The Version property enables conflict detection during sync:
    /// - Client sends ExpectedVersion with updates
    /// - Server rejects if Version != ExpectedVersion (conflict detected)
    /// - Version increments on each successful update
    /// 
    /// Implemented by:
    /// - Block (directly) - content blocks within notes/tasks
    /// - ICalendarEntity (extends) - calendar-based entities like Note and TaskItem
    /// </summary>
    public interface IVersionedSyncableEntity : ISyncableEntity
    {
        /// <summary>
        /// Monotonically increasing version number for optimistic concurrency.
        /// Starts at 1 on creation, increments on each update.
        /// </summary>
        long Version { get; }
    }
}
