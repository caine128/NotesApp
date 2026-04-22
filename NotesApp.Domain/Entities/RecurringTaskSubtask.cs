using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A subtask item used in two distinct roles, differentiated by which FK is set:
    ///
    /// 1. <b>Series template subtask</b> — <see cref="SeriesId"/> is set, <see cref="ExceptionId"/> is null.
    ///    Represents the default subtask list for all materialized occurrences of a <see cref="RecurringTaskSeries"/>.
    ///    <see cref="IsCompleted"/> is always <c>false</c> (factory enforces this).
    ///
    /// 2. <b>Exception subtask override</b> — <see cref="ExceptionId"/> is set, <see cref="SeriesId"/> is null.
    ///    Represents the complete desired subtask list for one specific virtual occurrence, as recorded
    ///    in a <see cref="RecurringTaskException"/>. When non-empty, these rows replace the template list
    ///    for that occurrence.
    ///
    /// Invariant: exactly one of SeriesId / ExceptionId is non-null.
    /// Enforced by the two factory methods and a DB check constraint.
    ///
    /// Cannot reuse <see cref="Subtask"/> because <see cref="Subtask.TaskId"/> is a non-nullable,
    /// non-empty invariant — there is no valid TaskId at template creation time.
    ///
    /// Design notes:
    /// - Implements IVersionedSyncableEntity (not ICalendarEntity) — carries no Date of its own.
    /// - UserId is stored directly for sync-query efficiency (same pattern as Block and Subtask).
    /// - Ordering uses fractional-index strings, same format as Subtask.Position.
    /// </summary>
    public sealed class RecurringTaskSubtask : Entity<Guid>, IVersionedSyncableEntity
    {
        // ENTITY CONSTANTS

        /// <summary>Maximum character length for subtask text.</summary>
        public const int MaxTextLength = 500;

        /// <summary>Maximum character length for the fractional-index position string.</summary>
        public const int MaxPositionLength = 100;

        // PROPERTIES

        /// <inheritdoc />
        public Guid UserId { get; private set; }

        /// <summary>
        /// FK to RecurringTaskSeries. Set when this row is a series template subtask.
        /// Null when this row is an exception subtask override.
        /// </summary>
        public Guid? SeriesId { get; private set; }

        /// <summary>
        /// FK to RecurringTaskException. Set when this row is an exception subtask override.
        /// Null when this row is a series template subtask.
        /// </summary>
        public Guid? ExceptionId { get; private set; }

        /// <summary>Display text for this subtask.</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>
        /// Whether this subtask has been completed.
        /// Always <c>false</c> for series template subtasks (enforced by <see cref="CreateForSeries"/>).
        /// May be <c>true</c> for exception subtask overrides.
        /// </summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Fractional-index string for ordering subtasks within the series or exception.
        /// Lexicographically sortable (e.g. "a0", "a1", "a0V").
        /// </summary>
        public string Position { get; private set; } = string.Empty;

        /// <inheritdoc />
        public long Version { get; private set; } = 1;

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private RecurringTaskSubtask() { }

        private RecurringTaskSubtask(Guid id,
                                     Guid userId,
                                     Guid? seriesId,
                                     Guid? exceptionId,
                                     string text,
                                     string position,
                                     bool isCompleted,
                                     DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            SeriesId = seriesId;
            ExceptionId = exceptionId;
            Text = text;
            Position = position;
            IsCompleted = isCompleted;
            Version = 1;
        }

        // FACTORIES

        /// <summary>
        /// Creates a new series template subtask linked to a <see cref="RecurringTaskSeries"/>.
        /// IsCompleted is always set to <c>false</c> — template subtasks start uncompleted.
        /// </summary>
        /// <param name="userId">The owner (tenant boundary). Must match the parent series UserId.</param>
        /// <param name="seriesId">The parent series. Must be non-empty.</param>
        /// <param name="text">Display text; leading/trailing whitespace is trimmed.</param>
        /// <param name="position">Fractional-index position string. Must be non-empty.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public static DomainResult<RecurringTaskSubtask> CreateForSeries(
            Guid userId,
            Guid seriesId,
            string? text,
            string? position,
            DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSubtask.UserId.Empty",
                    "UserId must be a non-empty GUID."));
            }

            if (seriesId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSubtask.SeriesId.Empty",
                    "SeriesId must be a non-empty GUID."));
            }

            var normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                errors.Add(new DomainError("RecurringSubtask.Text.Empty",
                    "Subtask text cannot be empty."));
            }

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (normalizedPosition.Length == 0)
            {
                errors.Add(new DomainError("RecurringSubtask.Position.Empty",
                    "Position must be a non-empty fractional-index string."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskSubtask>.Failure(errors);
            }

            var subtask = new RecurringTaskSubtask(
                Guid.NewGuid(),
                userId,
                seriesId: seriesId,
                exceptionId: null,
                normalizedText,
                normalizedPosition,
                isCompleted: false, // template subtasks always start uncompleted
                utcNow);

            return DomainResult<RecurringTaskSubtask>.Success(subtask);
        }

        /// <summary>
        /// Creates a new exception subtask override linked to a <see cref="RecurringTaskException"/>.
        /// These rows represent the complete desired subtask list for one specific virtual occurrence.
        /// </summary>
        /// <param name="userId">The owner (tenant boundary).</param>
        /// <param name="exceptionId">The parent exception. Must be non-empty.</param>
        /// <param name="text">Display text; leading/trailing whitespace is trimmed.</param>
        /// <param name="position">Fractional-index position string. Must be non-empty.</param>
        /// <param name="isCompleted">Completion state for this specific occurrence's subtask.</param>
        /// <param name="utcNow">Current UTC time used for audit fields.</param>
        public static DomainResult<RecurringTaskSubtask> CreateForException(
            Guid userId,
            Guid exceptionId,
            string? text,
            string? position,
            bool isCompleted,
            DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSubtask.UserId.Empty",
                    "UserId must be a non-empty GUID."));
            }

            if (exceptionId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSubtask.ExceptionId.Empty",
                    "ExceptionId must be a non-empty GUID."));
            }

            var normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                errors.Add(new DomainError("RecurringSubtask.Text.Empty",
                    "Subtask text cannot be empty."));
            }

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (normalizedPosition.Length == 0)
            {
                errors.Add(new DomainError("RecurringSubtask.Position.Empty",
                    "Position must be a non-empty fractional-index string."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskSubtask>.Failure(errors);
            }

            var subtask = new RecurringTaskSubtask(
                Guid.NewGuid(),
                userId,
                seriesId: null,
                exceptionId: exceptionId,
                normalizedText,
                normalizedPosition,
                isCompleted,
                utcNow);

            return DomainResult<RecurringTaskSubtask>.Success(subtask);
        }

        // BEHAVIOURS

        /// <summary>
        /// Updates the display text of this subtask.
        /// Increments <see cref="Version"/> so sync clients can detect the change.
        /// </summary>
        public DomainResult UpdateText(string? text, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("RecurringSubtask.Deleted",
                    "Cannot update a deleted subtask."));
            }

            var normalizedText = text?.Trim() ?? string.Empty;

            if (normalizedText.Length == 0)
            {
                return DomainResult.Failure(new DomainError("RecurringSubtask.Text.Empty",
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
        public DomainResult SetCompleted(bool isCompleted, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("RecurringSubtask.Deleted",
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
        /// Updates the fractional-index position of this subtask.
        /// Increments <see cref="Version"/> so sync clients can detect the change.
        /// </summary>
        public DomainResult UpdatePosition(string? position, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(new DomainError("RecurringSubtask.Deleted",
                    "Cannot update a deleted subtask."));
            }

            var normalizedPosition = position?.Trim() ?? string.Empty;

            if (normalizedPosition.Length == 0)
            {
                return DomainResult.Failure(new DomainError("RecurringSubtask.Position.Empty",
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
