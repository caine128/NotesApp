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
    public sealed class TaskItem : Entity<Guid>
    {
        public Guid UserId { get; private set; }

        public DateOnly Date { get; private set; }

        public string Title { get; private set; } = string.Empty;

        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Optional reminder time in UTC for notifications.
        /// </summary>
        public DateTime? ReminderAtUtc { get; private set; }

        private TaskItem()
        {
        }

        private TaskItem(Guid id, Guid userId, DateOnly date, string title, DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            Date = date;
            Title = title;
            IsCompleted = false;
        }

        // FACTORY

        public static DomainResult<TaskItem> Create(Guid userId, DateOnly date, string? title, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("Task.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("Task.Title.Empty", "Task title cannot be empty."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<TaskItem>.Failure(errors);
            }

            var id = Guid.NewGuid();
            var task = new TaskItem(id, userId, date, normalizedTitle, utcNow);
            return DomainResult<TaskItem>.Success(task);
        }

        // BEHAVIOURS

        public DomainResult Update(string? title, DateOnly date, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;

            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("Task.Title.Empty", "Task title cannot be empty."));
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
                Touch(utcNow);
            }

            // Idempotent
            return DomainResult.Success();
        }

        public DomainResult SetReminder(DateTime? reminderAtUtc, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("Task.Deleted", "Cannot set reminder on a deleted task.")
                );
            }

            ReminderAtUtc = reminderAtUtc;
            Touch(utcNow);

            return DomainResult.Success();
        }

        public DomainResult SoftDelete(DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Success();
            }

            MarkDeleted(utcNow);
            return DomainResult.Success();
        }

        public DomainResult RestoreTask(DateTime utcNow)
        {
            if (!IsDeleted)
            {
                return DomainResult.Success();
            }

            Restore(utcNow);
            return DomainResult.Success();
        }

        public string GetDisplayTitle()
            => string.IsNullOrWhiteSpace(Title) ? "(unnamed task)" : Title;
    }
}
