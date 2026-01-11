using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace NotesApp.Application.Sync.Models
{
    /// <summary>
    /// Entity types that can be synced.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncEntityType
    {
        Task,
        Note,
        Block
    }

    /// <summary>
    /// Types of conflicts that can occur during sync push operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncConflictType
    {
        /// <summary>
        /// Entity data failed validation rules.
        /// </summary>
        ValidationFailed,

        /// <summary>
        /// Entity was not found on the server.
        /// </summary>
        NotFound,

        /// <summary>
        /// Entity was deleted on the server before the operation could be applied.
        /// </summary>
        DeletedOnServer,

        /// <summary>
        /// Entity version mismatch - entity was modified by another client/device.
        /// </summary>
        VersionMismatch,

        /// <summary>
        /// Failed to create outbox message for the operation.
        /// </summary>
        OutboxFailed,

        /// <summary>
        /// Parent entity (for blocks) was not found.
        /// </summary>
        ParentNotFound
    }


    /// <summary>
    /// Client choices for resolving sync conflicts.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncResolutionChoice
    {
        /// <summary>
        /// Keep the server's version of the entity.
        /// </summary>
        KeepServer,

        /// <summary>
        /// Override server with client's version of the entity.
        /// </summary>
        KeepClient,

        /// <summary>
        /// Apply a merged version combining client and server changes.
        /// </summary>
        Merge
    }


    /// <summary>
    /// Status values for entity creation operations during sync push.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncPushCreatedStatus
    {
        /// <summary>
        /// Entity was successfully created on the server.
        /// </summary>
        Created,

        /// <summary>
        /// Entity creation failed due to validation or other errors.
        /// </summary>
        Failed
    }


    /// <summary>
    /// Status values for entity update operations during sync push.
    /// Binary outcome: Updated or Failed. When Failed, check Conflict.ConflictType for the reason.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncPushUpdatedStatus
    {
        /// <summary>
        /// Entity was successfully updated.
        /// </summary>
        Updated,

        /// <summary>
        /// Update operation failed. Check Conflict.ConflictType for the specific reason
        /// (NotFound, DeletedOnServer, VersionMismatch, or ValidationFailed).
        /// </summary>
        Failed
    }


    /// <summary>
    /// Status values for entity deletion operations during sync push.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncPushDeletedStatus
    {
        /// <summary>
        /// Entity was successfully deleted.
        /// </summary>
        Deleted,

        /// <summary>
        /// Entity was already deleted (idempotent delete).
        /// </summary>
        AlreadyDeleted,

        /// <summary>
        /// Entity was not found on the server (idempotent - desired end state achieved).
        /// </summary>
        NotFound,

        /// <summary>
        /// Delete operation failed due to infrastructure issues.
        /// Check Conflict.ConflictType for the specific reason (typically OutboxFailed).
        /// Entity was NOT deleted.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Status values for conflict resolution operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SyncConflictResolutionStatus
    {
        /// <summary>
        /// Server state was kept (client chose to accept server version).
        /// </summary>
        KeptServer,

        /// <summary>
        /// Entity was successfully updated with client's resolved data.
        /// </summary>
        Updated,

        /// <summary>
        /// Entity was not found on the server.
        /// </summary>
        NotFound,

        /// <summary>
        /// Entity was deleted on the server.
        /// </summary>
        DeletedOnServer,

        /// <summary>
        /// Resolution failed due to validation errors.
        /// </summary>
        ValidationFailed,

        /// <summary>
        /// Second-level conflict - server version changed again before resolution was applied.
        /// </summary>
        Conflict,

        /// <summary>
        /// Invalid entity type specified in the resolution request.
        /// </summary>
        InvalidEntityType
    }
}
