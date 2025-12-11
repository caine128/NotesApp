using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// A task (to-do item) for a specific day.
    /// Invariants:
    /// - UserId must be non-empty.
    /// - Title must be non-empty.
    /// </summary>
    public sealed class TaskItem : Entity<Guid>, ICalendarEntity
    {
        public Guid UserId { get; private set; }

        public DateOnly Date { get; private set; }

        public string Title { get; private set; } = string.Empty;

        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Optional free-text description/details for the task.
        /// </summary>
        public string? Description { get; private set; }

        /// <summary>
        /// Optional local start time of the task.
        /// </summary>
        public TimeOnly? StartTime { get; private set; }

        /// <summary>
        /// Optional local end time of the task.
        /// </summary>
        public TimeOnly? EndTime { get; private set; }

        /// <summary>
        /// Optional textual location (office, client name, address, etc.).
        /// </summary>
        public string? Location { get; private set; }

        /// <summary>
        /// Optional travel time to reach the location (used for calendar visualization).
        /// </summary>
        public TimeSpan? TravelTime { get; private set; }

        /// <summary>
        /// Optional reminder time in UTC for notifications.
        /// </summary>
        public DateTime? ReminderAtUtc { get; private set; }

        /// <summary>
        /// Monotonic business version used for sync/conflict detection.
        /// Starts at 1 and increments on every meaningful mutation.
        /// </summary>
        public long Version { get; private set; } = 1;

        /// <summary>
        /// When the user explicitly acknowledged the reminder (e.g. tapped the notification).
        /// </summary>
        public DateTime? ReminderAcknowledgedAtUtc { get; private set; }

        /// <summary>
        /// When a reminder push was actually sent by the system.
        /// </summary>
        public DateTime? ReminderSentAtUtc { get; private set; }

        private TaskItem()
        {
        }

        private TaskItem(Guid id,
                         Guid userId,
                         DateOnly date,
                         string title,
                         string? description,
                         TimeOnly? startTime,
                         TimeOnly? endTime,
                         string? location,
                         TimeSpan? travelTime,
                         DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            Date = date;
            Title = title;
            Description = description;
            StartTime = startTime;
            EndTime = endTime;
            Location = location;
            TravelTime = travelTime;
            IsCompleted = false;
            Version = 1;
        }

        // FACTORY

        public static DomainResult<TaskItem> Create(Guid userId,
                                                   DateOnly date,
                                                   string? title,
                                                   string? description,
                                                   TimeOnly? startTime,
                                                   TimeOnly? endTime,
                                                   string? location,
                                                   TimeSpan? travelTime,
                                                   DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;
            var normalizedDescription = description?.Trim();
            var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();


            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("Task.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("Task.Title.Empty", "Task title cannot be empty."));
            }

            if (date == default)
            {
                errors.Add(new DomainError("Task.Date.Default", "Date must be a valid calendar date."));
            }

            // Optional: basic time sanity check
            if (startTime.HasValue && endTime.HasValue && endTime < startTime)
            {
                errors.Add(new DomainError("Task.Time.Invalid", "EndTime cannot be earlier than StartTime."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<TaskItem>.Failure(errors);
            }

            var id = Guid.NewGuid();

            var task = new TaskItem(id,
                                    userId,
                                    date,
                                    normalizedTitle,
                                    normalizedDescription,
                                    startTime,
                                    endTime,
                                    normalizedLocation,
                                    travelTime,
                                    utcNow);

            return DomainResult<TaskItem>.Success(task);
        }


        // BEHAVIOURS
        public DomainResult Update(string? title,
                                    DateOnly date,
                                    string? description,
                                    TimeOnly? startTime,
                                    TimeOnly? endTime,
                                    string? location,
                                    TimeSpan? travelTime,
                                    DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;
            var normalizedDescription = description?.Trim();
            var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();


            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("Task.Title.Empty", "Task title cannot be empty."));
            }

            if (date == default)
            {
                errors.Add(new DomainError("Task.Date.Default", "Date must be a valid calendar date."));
            }

            if (startTime.HasValue && endTime.HasValue && endTime < startTime)
            {
                errors.Add(new DomainError("Task.Time.Invalid", "EndTime cannot be earlier than StartTime."));
            }

            if (IsDeleted)
            {
                errors.Add(new DomainError("Task.Deleted", "Cannot update a deleted task."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            Title = normalizedTitle;
            Date = date;
            Description = normalizedDescription;
            StartTime = startTime;
            EndTime = endTime;
            Location = normalizedLocation;
            TravelTime = travelTime;

            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        public DomainResult MarkCompleted(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot complete a deleted task.")
                );
            }

            if (!IsCompleted)
            {
                IsCompleted = true;
                IncrementVersion();
                Touch(utcNow);
            }

            // Idempotent: completing already completed is OK.
            return DomainResult.Success();
        }

        public DomainResult MarkPending(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot mark pending a deleted task.")
                );
            }

            if (IsCompleted)
            {
                IsCompleted = false;
                IncrementVersion();
                Touch(utcNow);
            }

            // Idempotent
            return DomainResult.Success();
        }


        public DomainResult AcknowledgeReminder(DateTime acknowledgedAtUtc, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot acknowledge reminder on a deleted task.")
                );
            }

            if (!ReminderAtUtc.HasValue)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Reminder.NotSet", "Cannot acknowledge reminder when no reminder is set.")
                );
            }

            // Idempotent: if already acknowledged, do nothing.
            if (ReminderAcknowledgedAtUtc.HasValue)
            {
                return DomainResult.Success();
            }

            ReminderAcknowledgedAtUtc = acknowledgedAtUtc;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        public DomainResult MarkReminderSent(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot mark reminder as sent for a deleted task.")
                );
            }

            if (!ReminderAtUtc.HasValue)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Reminder.NotSet", "Cannot mark reminder as sent when no reminder is set.")
                );
            }

            // Idempotent: if already marked as sent, do nothing.
            if (ReminderSentAtUtc.HasValue)
            {
                return DomainResult.Success();
            }

            ReminderSentAtUtc = utcNow;
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

        /// <summary>
        /// Clears reminder tracking (ack/sent) after the reminder definition changes.
        /// </summary>
        private void ResetReminderTracking(DateTime utcNow)
        {
            ReminderAcknowledgedAtUtc = null;
            ReminderSentAtUtc = null;

        }



        public DomainResult SetReminder(DateTime? reminderAtUtc, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot set reminder on a deleted task.")
                );
            }

            // Idempotent: if the reminder value is unchanged, do nothing.
            if (ReminderAtUtc == reminderAtUtc)
            {
                return DomainResult.Success();
            }

            ReminderAtUtc = reminderAtUtc;

            // When the reminder changes (set, changed, or cleared),
            // we clear tracking and bump version.
            ResetReminderTracking(utcNow);
            IncrementVersion();
            Touch(utcNow);

            return DomainResult.Success();
        }

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

        public DomainResult RestoreTask(DateTime utcNow)
        {
            if (!IsDeleted)
            {
                return DomainResult.Success();
            }

            IncrementVersion();
            Restore(utcNow);
            return DomainResult.Success();
        }

        public string GetDisplayTitle()
            => string.IsNullOrWhiteSpace(Title) ? "(unnamed task)" : Title;

        private void IncrementVersion()
        {
            Version++;
        }
    }


}
