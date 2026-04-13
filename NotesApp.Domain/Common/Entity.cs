using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Base implementation for entities to standardize Id, audit fields, soft delete, and concurrency.
    /// </summary>
    public abstract class Entity<TId> : IEntity<TId>
    {
        public TId Id { get; protected set; } = default!;
        public DateTime CreatedAtUtc { get; protected set; }
        public DateTime UpdatedAtUtc { get; protected set; }
        public bool IsDeleted { get; protected set; }

        /// <summary>
        /// Used by EF Core for optimistic concurrency control.
        /// </summary>
        public byte[] RowVersion { get; protected set; } = [];

        protected Entity()
        {
            // Parameterless constructor for EF Core
        }

        protected Entity(TId id, DateTime utcNow)
        {
            Id = id;
            CreatedAtUtc = utcNow;
            UpdatedAtUtc = utcNow;
            IsDeleted = false;
        }

        /// <summary>
        /// Mark the entity as deleted. 
        /// Intended to be called from derived domain methods that already did validation.
        /// </summary>
        protected void MarkDeleted(DateTime utcNow)
        {
            if (!IsDeleted)
            {
                IsDeleted = true;
                UpdatedAtUtc = utcNow;
            }
        }

        /// <summary>
        /// Restore the entity from a deleted state.
        /// Intended to be called from derived domain methods that already did validation.
        /// </summary>
        protected void Restore(DateTime utcNow)
        {
            if (IsDeleted)
            {
                IsDeleted = false;
                UpdatedAtUtc = utcNow;
            }
        }

        /// <summary>
        /// Update the "last modified" timestamp.
        /// </summary>
        protected void Touch(DateTime utcNow)
        {
            UpdatedAtUtc = utcNow;
        }

        /// <summary>
        /// Overwrites the in-memory RowVersion with the client-supplied token before the entity
        /// is attached for persistence. EF Core will then use this value as OriginalValue in the
        /// generated WHERE clause, enabling stale-page detection.
        /// Only call this in web edit handlers immediately before Repository.Update().
        /// </summary>
        public void ApplyClientRowVersion(byte[] rowVersion) => RowVersion = rowVersion;
    }
}
