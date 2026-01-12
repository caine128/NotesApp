using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain
{
    /// <summary>
    /// Base interface for all entities that participate in client-server synchronization.
    /// 
    /// Provides:
    /// - UserId for tenant isolation (each user's data is separate)
    /// - Inherits from IEntity&lt;Guid&gt; which provides Id, IsDeleted, timestamps, and RowVersion
    /// 
    /// Implemented by:
    /// - Asset (directly) - immutable binary attachments
    /// - IVersionedSyncableEntity (extends) - entities with optimistic concurrency
    /// </summary>
    public interface ISyncableEntity : IEntity<Guid>
    {
        /// <summary>
        /// The user who owns this entity (tenant boundary).
        /// Used for data isolation and sync filtering.
        /// </summary>
        Guid UserId { get; }
    }
}
