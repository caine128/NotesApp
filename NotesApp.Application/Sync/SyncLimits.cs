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

        // Push (/api/sync/push) — per-entity collection limits

        /// <summary>
        /// Maximum total number of items allowed across all push collections
        /// in a single /sync/push call.
        /// </summary>
        public const int PushMaxTotalItems = 2000;

        /// <summary>
        /// Maximum number of task items allowed in each push collection
        /// (Tasks.Created, Tasks.Updated, Tasks.Deleted).
        /// </summary>
        public const int PushMaxTasks = 500;

        /// <summary>
        /// Maximum number of note items allowed in each push collection
        /// (Notes.Created, Notes.Updated, Notes.Deleted).
        /// </summary>
        public const int PushMaxNotes = 500;

        /// <summary>
        /// Maximum number of block items allowed in each push collection
        /// (Blocks.Created, Blocks.Updated, Blocks.Deleted).
        /// </summary>
        public const int PushMaxBlocks = 500;

        /// <summary>
        /// Maximum number of category items allowed in each push collection
        /// (Categories.Created, Categories.Updated, Categories.Deleted).
        /// </summary>
        public const int PushMaxCategories = 500;

        // Subtasks

        /// <summary>
        /// Maximum number of subtask items allowed in each push collection
        /// (Subtasks.Created, Subtasks.Updated, Subtasks.Deleted).
        /// </summary>
        public const int PushMaxSubtasks = 500;

        // Attachments

        /// <summary>
        /// Maximum number of attachment deletions allowed in Attachments.Deleted per push.
        /// </summary>
        public const int PushMaxAttachmentDeletes = 500;

        // Recurring entities

        /// <summary>
        /// Maximum number of recurring root items allowed in each push collection
        /// (RecurringRoots.Created, RecurringRoots.Deleted).
        /// </summary>
        public const int PushMaxRecurringRoots = 500;

        /// <summary>
        /// Maximum number of recurring series items allowed in each push collection
        /// (RecurringSeries.Created, RecurringSeries.Updated, RecurringSeries.Deleted).
        /// </summary>
        public const int PushMaxRecurringSeries = 500;

        /// <summary>
        /// Maximum number of recurring series subtask items allowed in each push collection
        /// (RecurringSeriesSubtasks.Created, RecurringSeriesSubtasks.Updated, RecurringSeriesSubtasks.Deleted).
        /// </summary>
        public const int PushMaxRecurringSeriesSubtasks = 500;

        /// <summary>
        /// Maximum number of recurring exception items allowed in each push collection
        /// (RecurringExceptions.Created, RecurringExceptions.Updated, RecurringExceptions.Deleted).
        /// </summary>
        public const int PushMaxRecurringExceptions = 500;

        /// <summary>
        /// Maximum number of recurring attachment deletions allowed in RecurringAttachments.Deleted per push.
        /// </summary>
        public const int PushMaxRecurringAttachmentDeletes = 500;
    }
}
