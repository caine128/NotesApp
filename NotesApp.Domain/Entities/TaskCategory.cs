using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A user-defined category label that can be assigned to tasks.
    /// Categories are per-user and fully user-managed (add, rename, delete).
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// - Name must be non-empty after trimming.
    ///
    /// Design notes:
    /// - Implements IVersionedSyncableEntity so the sync protocol can detect
    ///   concurrent renames across devices using ExpectedVersion.
    /// - Does NOT implement ICalendarEntity — categories are not date-scoped.
    /// - FK retention: when a category is soft-deleted, tasks that reference it
    ///   retain the CategoryId FK. The REST delete path calls
    ///   ClearCategoryFromTasksAsync to null out affected tasks on the server.
    ///   The sync push path delegates that responsibility to the mobile client.
    /// </summary>
    public sealed class TaskCategory : Entity<Guid>, IVersionedSyncableEntity
    {
        // ENTITY CONSTANTS

        /// <summary>Maximum character length for a category name.</summary>
        public const int MaxNameLength = 100;

        // PROPERTIES

        /// <inheritdoc />
        public Guid UserId { get; private set; }

        /// <summary>Display name of the category (e.g. "Work", "Lifestyle").</summary>
        public string Name { get; private set; } = string.Empty;

        /// <inheritdoc />
        public long Version { get; private set; } = 1;

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private TaskCategory() { }

        private TaskCategory(Guid id, Guid userId, string name, DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            Name = name;
            Version = 1;
        }

        // FACTORY

        /// <summary>
        /// Creates a new <see cref="TaskCategory"/> for the given user.
        /// Returns a failure result when any invariant is violated.
        /// </summary>
        /// <param name="userId">The owner of this category (tenant boundary).</param>
        /// <param name="name">The display name; leading/trailing whitespace is trimmed.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public static DomainResult<TaskCategory> Create(Guid userId, string? name, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedName = name?.Trim() ?? string.Empty;

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("TaskCategory.UserId.Empty",
                    "UserId must be a non-empty GUID."));
            }

            if (normalizedName.Length == 0)
            {
                errors.Add(new DomainError("TaskCategory.Name.Empty",
                    "Category name cannot be empty."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<TaskCategory>.Failure(errors);
            }

            var category = new TaskCategory(Guid.NewGuid(), userId, normalizedName, utcNow);
            return DomainResult<TaskCategory>.Success(category);
        }

        // BEHAVIOURS

        /// <summary>
        /// Renames this category.
        /// Increments <see cref="Version"/> so sync clients can detect the change.
        /// </summary>
        /// <param name="name">New display name; leading/trailing whitespace is trimmed.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public DomainResult Update(string? name, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedName = name?.Trim() ?? string.Empty;

            if (normalizedName.Length == 0)
            {
                errors.Add(new DomainError("TaskCategory.Name.Empty",
                    "Category name cannot be empty."));
            }

            if (IsDeleted)
            {
                errors.Add(new DomainError("TaskCategory.Deleted",
                    "Cannot update a deleted category."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            Name = normalizedName;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Soft-deletes this category.
        /// Idempotent: soft-deleting an already-deleted category returns success.
        /// </summary>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
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

        // PRIVATE HELPERS

        private void IncrementVersion() => Version++;
    }
}
