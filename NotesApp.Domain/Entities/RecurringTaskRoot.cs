using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Logical identity anchor for a recurring task family.
    /// Groups all series segments so "delete all" / "edit all" have a stable target
    /// across splits caused by ThisAndFollowing edits.
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// </summary>
    public sealed class RecurringTaskRoot : Entity<Guid>, IVersionedSyncableEntity
    {
        /// <summary>
        /// The user who owns this recurring task family.
        /// </summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// Monotonic business version used for sync/conflict detection.
        /// Starts at 1 and increments on every meaningful mutation.
        /// </summary>
        public long Version { get; private set; } = 1;

        private RecurringTaskRoot()
        {
            // Parameterless constructor for EF Core
        }

        private RecurringTaskRoot(Guid id, Guid userId, DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
        }

        // FACTORY

        /// <summary>
        /// Creates a new RecurringTaskRoot for the specified user.
        /// </summary>
        public static DomainResult<RecurringTaskRoot> Create(Guid userId, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringRoot.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskRoot>.Failure(errors);
            }

            var id = Guid.NewGuid();
            return DomainResult<RecurringTaskRoot>.Success(new RecurringTaskRoot(id, userId, utcNow));
        }

        // BEHAVIOURS

        /// <summary>
        /// Soft-deletes the root. Idempotent — calling on an already-deleted root is a no-op.
        /// </summary>
        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Success();
            }

            IncrementVersion();
            MarkDeleted(utcNow);
            return DomainResult.Success();
        }

        private void IncrementVersion() => Version++;
    }
}
