using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync
{
    /// <summary>
    /// Central place for sync-related size limits.
    /// We keep separate constants for pull vs push so their semantics stay clear.
    /// </summary>
    internal static class SyncLimits
    {
        // Pull (/api/sync/changes)

        /// <summary>
        /// Default per-entity maximum number of items returned by sync pull
        /// when the client does not specify maxItemsPerEntity.
        /// </summary>
        public const int DefaultPullMaxItemsPerEntity = 500;

        /// <summary>
        /// Hard upper bound on maxItemsPerEntity that a client is allowed to request.
        /// </summary>
        public const int HardPullMaxItemsPerEntity = 1000;

        // Push (/api/sync/push)

        /// <summary>
        /// Maximum number of items allowed in each individual push collection
        /// (Tasks.Created, Tasks.Updated, Notes.Created, etc.).
        /// </summary>
        public const int PushMaxItemsPerEntity = 500;

        /// <summary>
        /// Maximum total number of items allowed across all push collections
        /// in a single /sync/push call.
        /// </summary>
        public const int PushMaxTotalItems = 2000;
    }
}
