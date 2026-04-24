using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// Records a deviation from the series recurrence rule for one specific canonical occurrence date.
    ///
    /// Serves two roles distinguished by <see cref="IsDeletion"/>:
    /// - <b>Deletion exception</b> (<c>IsDeletion = true</c>): skip this occurrence entirely.
    ///   All override fields are null.
    /// - <b>Override exception</b> (<c>IsDeletion = false</c>): apply field-level overrides to this
    ///   occurrence. Any null override field means "inherit from the series template".
    ///
    /// Works for both virtual (non-materialized) and materialized occurrences.
    /// For materialized occurrences, <see cref="MaterializedTaskItemId"/> links back to the TaskItem.
    ///
    /// Subtask overrides for virtual occurrences are stored as <see cref="RecurringTaskSubtask"/> rows
    /// with <see cref="RecurringTaskSubtask.ExceptionId"/> set. They are loaded via separate repository
    /// calls (no EF navigation properties — follows existing conventions).
    ///
    /// Uniqueness: one exception per (SeriesId, OccurrenceDate).
    /// Enforced by a unique DB index on (SeriesId, OccurrenceDate) FILTERED [IsDeleted]=0
    /// and by the application layer pre-checking before creating a duplicate.
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// - SeriesId must be non-empty.
    /// - OccurrenceDate must not be default.
    /// - If EndTime override is set and StartTime override is also set, EndTime must not be earlier than StartTime.
    /// </summary>
    public sealed class RecurringTaskException : Entity<Guid>, IVersionedSyncableEntity
    {
        // IDENTITY

        /// <summary>The user who owns this exception (tenant boundary).</summary>
        public Guid UserId { get; private set; }

        /// <summary>FK to the RecurringTaskSeries this exception belongs to.</summary>
        public Guid SeriesId { get; private set; }

        /// <summary>
        /// The canonical (recurrence-engine-generated) date that this exception targets.
        /// Immutable after creation.
        /// </summary>
        public DateOnly OccurrenceDate { get; private set; }

        // TYPE FLAG

        /// <summary>
        /// When <c>true</c>, this occurrence is suppressed (skipped).
        /// When <c>false</c>, the occurrence is rendered using the override fields
        /// (inheriting from the series template for any null override fields).
        /// </summary>
        public bool IsDeletion { get; private set; }

        // OVERRIDE FIELDS (all nullable — null means "inherit from series template")

        /// <summary>Title override. Null = inherit from series template.</summary>
        public string? OverrideTitle { get; private set; }

        /// <summary>Description override. Null = inherit from series template.</summary>
        public string? OverrideDescription { get; private set; }

        /// <summary>
        /// Moved occurrence date override. Null = occurrence stays on OccurrenceDate.
        /// When set, the materialized TaskItem.Date will use this value instead of OccurrenceDate.
        /// </summary>
        public DateOnly? OverrideDate { get; private set; }

        /// <summary>Start time override. Null = inherit from series template.</summary>
        public TimeOnly? OverrideStartTime { get; private set; }

        /// <summary>End time override. Null = inherit from series template.</summary>
        public TimeOnly? OverrideEndTime { get; private set; }

        /// <summary>Location override. Null = inherit from series template.</summary>
        public string? OverrideLocation { get; private set; }

        /// <summary>Travel time override. Null = inherit from series template.</summary>
        public TimeSpan? OverrideTravelTime { get; private set; }

        /// <summary>Category override. Null = inherit from series template.</summary>
        public Guid? OverrideCategoryId { get; private set; }

        /// <summary>Priority override. Null = inherit from series template.</summary>
        public TaskPriority? OverridePriority { get; private set; }

        /// <summary>Meeting link override. Null = inherit from series template.</summary>
        public string? OverrideMeetingLink { get; private set; }

        /// <summary>Reminder UTC override. Null = inherit from series template (computed from ReminderOffsetMinutes).</summary>
        public DateTime? OverrideReminderAtUtc { get; private set; }

        /// <summary>
        /// Completion state for this specific occurrence.
        /// Stored directly on the exception (not nullable) because the series template
        /// has no completion state to inherit from — each occurrence tracks its own.
        /// Defaults to false (not completed).
        /// </summary>
        public bool IsCompleted { get; private set; }

        // ATTACHMENT OVERRIDE FLAG

        /// <summary>
        /// When <c>true</c>, this occurrence's attachment list is managed independently via
        /// <see cref="RecurringTaskAttachment"/> rows linked to this exception's ID.
        /// When <c>false</c>, the occurrence inherits the series template attachments.
        ///
        /// Set to <c>true</c> the first time an attachment upload or delete targets this specific
        /// occurrence. Once set, it is never cleared — even if all exception attachments are later
        /// deleted — to prevent "snap-back" to the series template attachment list.
        /// </summary>
        public bool HasAttachmentOverride { get; private set; }

        // MATERIALIZATION LINK

        /// <summary>
        /// FK to TaskItem.Id when this occurrence has been materialized.
        /// Null for virtual (non-materialized) occurrences.
        /// Set NULL in the DB when the linked TaskItem is deleted.
        /// Used by "edit all" to identify individually-modified occurrences without timestamp heuristics.
        /// </summary>
        public Guid? MaterializedTaskItemId { get; private set; }

        // VERSION

        /// <summary>
        /// Monotonic business version used for sync/conflict detection.
        /// Starts at 1 and increments on every meaningful mutation.
        /// </summary>
        public long Version { get; private set; } = 1;

        // CONSTRUCTORS

        /// <summary>Parameterless constructor required by EF Core.</summary>
        private RecurringTaskException() { }

        private RecurringTaskException(Guid id,
                                       Guid userId,
                                       Guid seriesId,
                                       DateOnly occurrenceDate,
                                       bool isDeletion,
                                       string? overrideTitle,
                                       string? overrideDescription,
                                       DateOnly? overrideDate,
                                       TimeOnly? overrideStartTime,
                                       TimeOnly? overrideEndTime,
                                       string? overrideLocation,
                                       TimeSpan? overrideTravelTime,
                                       Guid? overrideCategoryId,
                                       TaskPriority? overridePriority,
                                       string? overrideMeetingLink,
                                       DateTime? overrideReminderAtUtc,
                                       bool isCompleted,
                                       Guid? materializedTaskItemId,
                                       DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            SeriesId = seriesId;
            OccurrenceDate = occurrenceDate;
            IsDeletion = isDeletion;
            OverrideTitle = overrideTitle;
            OverrideDescription = overrideDescription;
            OverrideDate = overrideDate;
            OverrideStartTime = overrideStartTime;
            OverrideEndTime = overrideEndTime;
            OverrideLocation = overrideLocation;
            OverrideTravelTime = overrideTravelTime;
            OverrideCategoryId = overrideCategoryId;
            OverridePriority = overridePriority;
            OverrideMeetingLink = overrideMeetingLink;
            OverrideReminderAtUtc = overrideReminderAtUtc;
            IsCompleted = isCompleted;
            MaterializedTaskItemId = materializedTaskItemId;
        }

        // FACTORIES

        /// <summary>
        /// Creates a deletion exception — marks the specified occurrence as skipped.
        /// </summary>
        public static DomainResult<RecurringTaskException> CreateDeletion(Guid userId,
                                                                          Guid seriesId,
                                                                          DateOnly occurrenceDate,
                                                                          Guid? materializedTaskItemId,
                                                                          DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringException.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (seriesId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringException.SeriesId.Empty", "SeriesId must be a non-empty GUID."));
            }

            if (occurrenceDate == default)
            {
                errors.Add(new DomainError("RecurringException.OccurrenceDate.Default", "OccurrenceDate must be a valid calendar date."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskException>.Failure(errors);
            }

            return DomainResult<RecurringTaskException>.Success(
                new RecurringTaskException(
                    Guid.NewGuid(),
                    userId,
                    seriesId,
                    occurrenceDate,
                    isDeletion: true,
                    overrideTitle: null,
                    overrideDescription: null,
                    overrideDate: null,
                    overrideStartTime: null,
                    overrideEndTime: null,
                    overrideLocation: null,
                    overrideTravelTime: null,
                    overrideCategoryId: null,
                    overridePriority: null,
                    overrideMeetingLink: null,
                    overrideReminderAtUtc: null,
                    isCompleted: false,      // deletion exceptions have no completion state
                    materializedTaskItemId,
                    utcNow));
        }

        /// <summary>
        /// Creates a field-override exception for a specific occurrence.
        /// Any null override field means "inherit from the series template".
        /// </summary>
        public static DomainResult<RecurringTaskException> CreateOverride(Guid userId,
                                                                          Guid seriesId,
                                                                          DateOnly occurrenceDate,
                                                                          string? overrideTitle,
                                                                          string? overrideDescription,
                                                                          DateOnly? overrideDate,
                                                                          TimeOnly? overrideStartTime,
                                                                          TimeOnly? overrideEndTime,
                                                                          string? overrideLocation,
                                                                          TimeSpan? overrideTravelTime,
                                                                          Guid? overrideCategoryId,
                                                                          TaskPriority? overridePriority,
                                                                          string? overrideMeetingLink,
                                                                          DateTime? overrideReminderAtUtc,
                                                                          bool isCompleted,
                                                                          Guid? materializedTaskItemId,
                                                                          DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringException.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (seriesId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringException.SeriesId.Empty", "SeriesId must be a non-empty GUID."));
            }

            if (occurrenceDate == default)
            {
                errors.Add(new DomainError("RecurringException.OccurrenceDate.Default", "OccurrenceDate must be a valid calendar date."));
            }

            if (overrideStartTime.HasValue && overrideEndTime.HasValue && overrideEndTime < overrideStartTime)
            {
                errors.Add(new DomainError("RecurringException.Time.Invalid", "OverrideEndTime cannot be earlier than OverrideStartTime."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskException>.Failure(errors);
            }

            var normalizedTitle = string.IsNullOrWhiteSpace(overrideTitle) ? null : overrideTitle.Trim();
            var normalizedDescription = overrideDescription?.Trim();
            var normalizedLocation = string.IsNullOrWhiteSpace(overrideLocation) ? null : overrideLocation.Trim();
            var normalizedMeetingLink = string.IsNullOrWhiteSpace(overrideMeetingLink) ? null : overrideMeetingLink.Trim();

            return DomainResult<RecurringTaskException>.Success(
                new RecurringTaskException(
                    Guid.NewGuid(),
                    userId,
                    seriesId,
                    occurrenceDate,
                    isDeletion: false,
                    normalizedTitle,
                    normalizedDescription,
                    overrideDate,
                    overrideStartTime,
                    overrideEndTime,
                    normalizedLocation,
                    overrideTravelTime,
                    overrideCategoryId,
                    overridePriority,
                    normalizedMeetingLink,
                    overrideReminderAtUtc,
                    isCompleted,
                    materializedTaskItemId,
                    utcNow));
        }

        // BEHAVIOURS

        /// <summary>
        /// Updates all override fields on an existing override exception.
        /// Not valid on deletion exceptions (they have no override fields to update).
        /// </summary>
        public DomainResult UpdateOverride(string? overrideTitle,
                                           string? overrideDescription,
                                           DateOnly? overrideDate,
                                           TimeOnly? overrideStartTime,
                                           TimeOnly? overrideEndTime,
                                           string? overrideLocation,
                                           TimeSpan? overrideTravelTime,
                                           Guid? overrideCategoryId,
                                           TaskPriority? overridePriority,
                                           string? overrideMeetingLink,
                                           DateTime? overrideReminderAtUtc,
                                           bool isCompleted,
                                           DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (IsDeleted)
            {
                errors.Add(new DomainError("RecurringException.Deleted", "Cannot update a deleted exception."));
            }

            if (IsDeletion)
            {
                errors.Add(new DomainError("RecurringException.IsDeletion",
                    "Cannot update override fields on a deletion exception."));
            }

            if (overrideStartTime.HasValue && overrideEndTime.HasValue && overrideEndTime < overrideStartTime)
            {
                errors.Add(new DomainError("RecurringException.Time.Invalid",
                    "OverrideEndTime cannot be earlier than OverrideStartTime."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            OverrideTitle = string.IsNullOrWhiteSpace(overrideTitle) ? null : overrideTitle.Trim();
            OverrideDescription = overrideDescription?.Trim();
            OverrideDate = overrideDate;
            OverrideStartTime = overrideStartTime;
            OverrideEndTime = overrideEndTime;
            OverrideLocation = string.IsNullOrWhiteSpace(overrideLocation) ? null : overrideLocation.Trim();
            OverrideTravelTime = overrideTravelTime;
            OverrideCategoryId = overrideCategoryId;
            OverridePriority = overridePriority;
            OverrideMeetingLink = string.IsNullOrWhiteSpace(overrideMeetingLink) ? null : overrideMeetingLink.Trim();
            OverrideReminderAtUtc = overrideReminderAtUtc;
            IsCompleted = isCompleted;

            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Records that this exception has been materialized into a TaskItem.
        /// Idempotent when the same taskItemId is provided again.
        /// </summary>
        public DomainResult SetMaterializedTaskItemId(Guid taskItemId, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("RecurringException.Deleted", "Cannot update a deleted exception."));
            }

            if (taskItemId == Guid.Empty)
            {
                return DomainResult.Failure(
                    new DomainError("RecurringException.TaskItemId.Empty", "TaskItemId must be a non-empty GUID."));
            }

            if (MaterializedTaskItemId == taskItemId)
            {
                return DomainResult.Success();
            }

            MaterializedTaskItemId = taskItemId;
            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Marks this exception as having its own managed attachment list.
        /// Idempotent — calling when already marked is a no-op.
        /// Sets <see cref="HasAttachmentOverride"/> = <c>true</c> and increments version.
        /// </summary>
        public void MarkAttachmentsOverridden(DateTime utcNow)
        {
            if (HasAttachmentOverride)
                return;

            HasAttachmentOverride = true;
            IncrementVersion();
            Touch(utcNow);
        }

        /// <summary>
        /// Soft-deletes this exception.
        /// Idempotent — calling on an already-deleted exception is a no-op.
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
