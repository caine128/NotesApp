using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A child item belonging to a single <see cref="TaskItem"/>.
    /// Supports text, completion status, and fractional-index ordering within the parent task.
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// - TaskId must be non-empty.
    /// - Text must be non-empty after trimming.
    /// - Position must be non-empty.
    ///
    /// Design notes:
    /// - Implements IVersionedSyncableEntity (not ICalendarEntity) — Subtask is a component
    ///   of a calendar entity and carries no Date of its own.
    /// - UserId is stored directly for sync-query efficiency (same pattern as Block).
    /// - Ordering uses fractional-index strings (e.g. "a0", "a1", "a0V") so that a single
    ///   reorder touches only the moved subtask rather than all siblings.
    /// - FK retention: TaskId is retained as-is when the parent task is soft-deleted.
    ///   The application layer handles cascading via SoftDeleteAllForTaskAsync.
    /// </summary>
    public sealed class Subtask : Entity<Guid>, IVersionedSyncableEntity
    {
        // ENTITY CONSTANTS

        /// <summary>Maximum character length for subtask text.</summary>
        public const int MaxTextLength = 500;

        /// <summary>Maximum character length for the fractional-index position string.</summary>
        public const int MaxPositionLength = 100;

        // PROPERTIES

        /// <inheritdoc />
        public Guid UserId { get; private set; }

        /// <summary>The parent task this subtask belongs to.</summary>
        public Guid TaskId { get; private set; }

        /// <summary>Display text for this subtask.</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>Whether this subtask has been completed.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Fractional-index string for ordering subtasks within the parent task.
        /// Lexicographically sortable (e.g. "a0", "a1", "a0V").
        /// </summary>
        public string Position { get; private set; } = string.Empty;

        /// <inheritdoc />
        public long Version { get; private set; } = 1;

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private Subtask() { }

        private Subtask(Guid id, Guid userId, Guid taskId, string text, string position, DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            TaskId = taskId;
            Text = text;
            Position = position;
            IsCompleted = false;
            Version = 1;
        }

        // FACTORY

        /// <summary>
        /// Creates a new <see cref="Subtask"/> for the given task.
        /// Returns a failure result when any invariant is violated.
        /// </summary>
        /// <param name="userId">The owner (tenant boundary). Must match the parent task's UserId.</param>
        /// <param name="taskId">The parent task. Must be non-empty.</param>
        /// <param name="text">Display text; leading/trailing whitespace is trimmed.</param>
        /// <param name="position">Fractional-index position string. Must be non-empty.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public static DomainResult<Subtask> Create(
            Guid userId,
            Guid taskId,
            string? text,
            string? position,
            DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("Subtask.UserId.Empty",
                    "UserId must be a non-empty GUID."));
            }

            if (taskId == Guid.Empty)
            {
                errors.Add(new DomainError("Subtask.TaskId.Empty",
                    "TaskId must be a non-empty GUID."));
            }

            var normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                errors.Add(new DomainError("Subtask.Text.Empty",
                    "Subtask text cannot be empty."));
            }

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (normalizedPosition.Length == 0)
            {
                errors.Add(new DomainError("Subtask.Position.Empty",
                    "Position must be a non-empty fractional-index string."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<Subtask>.Failure(errors);
            }

            var subtask = new Subtask(Guid.NewGuid(), userId, taskId, normalizedText, normalizedPosition, utcNow);
            return DomainResult<Subtask>.Success(subtask);
        }

        // BEHAVIOURS

        /// <summary>
        /// Updates the display text of this subtask.
        /// Increments <see cref="Version"/> so sync clients can detect the change.
        /// </summary>
        /// <param name="text">New display text; leading/trailing whitespace is trimmed.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public DomainResult UpdateText(string? text, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("Subtask.Deleted",
                    "Cannot update a deleted subtask."));
            }

            var normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                return DomainResult.Failure(new DomainError("Subtask.Text.Empty",
                    "Subtask text cannot be empty."));
            }

            Text = normalizedText;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Sets the completion state of this subtask.
        /// Idempotent: if the value is unchanged, Version is not incremented.
        /// </summary>
        /// <param name="isCompleted">New completion state.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public DomainResult SetCompleted(bool isCompleted, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("Subtask.Deleted",
                    "Cannot update a deleted subtask."));
            }

            if (IsCompleted == isCompleted)
            {
                return DomainResult.Success();
            }

            IsCompleted = isCompleted;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Updates the fractional-index position of this subtask within its parent task.
        /// Increments <see cref="Version"/> so sync clients can detect the change.
        /// </summary>
        /// <param name="position">New fractional-index string. Must be non-empty.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public DomainResult UpdatePosition(string? position, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("Subtask.Deleted",
                    "Cannot update a deleted subtask."));
            }

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (normalizedPosition.Length == 0)
            {
                return DomainResult.Failure(new DomainError("Subtask.Position.Empty",
                    "Position must be a non-empty fractional-index string."));
            }

            Position = normalizedPosition;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Soft-deletes this subtask.
        /// Idempotent: soft-deleting an already-deleted subtask returns success.
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
