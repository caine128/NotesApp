using NotesApp.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Entities
{
    /// <summary>
    /// One segment of a recurring task pattern.
    /// A root may have multiple series segments created when "this and following" edits split the pattern.
    ///
    /// Invariants:
    /// - UserId must be non-empty.
    /// - RootId must be non-empty.
    /// - Title must be non-empty.
    /// - RRuleString must be non-empty.
    /// - StartsOnDate must not be default.
    /// - If EndTime is set, it must not be earlier than StartTime (when both are provided).
    /// - EndsBeforeDate, when set, must be after StartsOnDate.
    /// </summary>
    public sealed class RecurringTaskSeries : Entity<Guid>, IVersionedSyncableEntity
    {
        // ENTITY CONSTANTS
        public const int MaxTitleLength = 200;
        public const int MaxRRuleStringLength = 500;

        // IDENTITY

        /// <summary>The user who owns this series.</summary>
        public Guid UserId { get; private set; }

        /// <summary>
        /// FK to RecurringTaskRoot — the stable identity anchor shared by all segments
        /// created from the same original recurring task.
        /// </summary>
        public Guid RootId { get; private set; }

        // RECURRENCE RULE

        /// <summary>
        /// RFC 5545 RRULE body without DTSTART or UNTIL, e.g. "FREQ=WEEKLY;BYDAY=MO,WE,FR;INTERVAL=2".
        /// COUNT is included here for AfterCount end condition.
        /// DTSTART is stored separately as StartsOnDate; UNTIL is stored separately as EndsBeforeDate.
        /// </summary>
        public string RRuleString { get; private set; } = string.Empty;

        /// <summary>
        /// Inclusive start date for this series segment (DTSTART equivalent).
        /// Required for SQL range queries and Ical.Net evaluation.
        /// </summary>
        public DateOnly StartsOnDate { get; private set; }

        /// <summary>
        /// Exclusive upper bound for this series segment.
        /// Set by Terminate() when a ThisAndFollowing edit splits the series.
        /// Also set directly for the OnDate end condition.
        /// Null means the series has no explicit end (Never or AfterCount via COUNT in RRuleString).
        /// </summary>
        public DateOnly? EndsBeforeDate { get; private set; }

        // TASK TEMPLATE FIELDS

        /// <summary>Title template for materialized TaskItems.</summary>
        public string Title { get; private set; } = string.Empty;

        /// <summary>Optional description template.</summary>
        public string? Description { get; private set; }

        /// <summary>Optional local start time template.</summary>
        public TimeOnly? StartTime { get; private set; }

        /// <summary>Optional local end time template.</summary>
        public TimeOnly? EndTime { get; private set; }

        /// <summary>Optional location template.</summary>
        public string? Location { get; private set; }

        /// <summary>Optional travel time template.</summary>
        public TimeSpan? TravelTime { get; private set; }

        /// <summary>Optional category reference template. Null = uncategorized.</summary>
        public Guid? CategoryId { get; private set; }

        /// <summary>Priority template. Defaults to Normal.</summary>
        public TaskPriority Priority { get; private set; } = TaskPriority.Normal;

        /// <summary>Optional meeting link template.</summary>
        public string? MeetingLink { get; private set; }

        // REMINDER

        /// <summary>
        /// Reminder offset in minutes before StartTime.
        /// Null means no reminder. Stored as an offset (not absolute UTC) because
        /// each occurrence falls on a different calendar date.
        /// </summary>
        public int? ReminderOffsetMinutes { get; private set; }

        // MATERIALIZATION TRACKING

        /// <summary>
        /// Latest date through which TaskItems have been created for this series.
        /// The horizon worker advances this forward on each poll cycle.
        /// </summary>
        public DateOnly MaterializedUpToDate { get; private set; }

        // VERSION

        /// <summary>
        /// Monotonic business version used for sync/conflict detection.
        /// Starts at 1 and increments on every meaningful mutation.
        /// </summary>
        public long Version { get; private set; } = 1;

        private RecurringTaskSeries()
        {
            // Parameterless constructor for EF Core
        }

        private RecurringTaskSeries(Guid id,
                                    Guid userId,
                                    Guid rootId,
                                    string rruleString,
                                    DateOnly startsOnDate,
                                    DateOnly? endsBeforeDate,
                                    string title,
                                    string? description,
                                    TimeOnly? startTime,
                                    TimeOnly? endTime,
                                    string? location,
                                    TimeSpan? travelTime,
                                    Guid? categoryId,
                                    TaskPriority priority,
                                    string? meetingLink,
                                    int? reminderOffsetMinutes,
                                    DateOnly materializedUpToDate,
                                    DateTime utcNow)
            : base(id, utcNow)
        {
            UserId = userId;
            RootId = rootId;
            RRuleString = rruleString;
            StartsOnDate = startsOnDate;
            EndsBeforeDate = endsBeforeDate;
            Title = title;
            Description = description;
            StartTime = startTime;
            EndTime = endTime;
            Location = location;
            TravelTime = travelTime;
            CategoryId = categoryId;
            Priority = priority;
            MeetingLink = meetingLink;
            ReminderOffsetMinutes = reminderOffsetMinutes;
            MaterializedUpToDate = materializedUpToDate;
        }

        // FACTORY

        /// <summary>
        /// Creates a new RecurringTaskSeries segment.
        /// </summary>
        /// <param name="userId">Owning user.</param>
        /// <param name="rootId">Logical root that groups all series segments for this recurring task.</param>
        /// <param name="rruleString">RFC 5545 RRULE body (no DTSTART/UNTIL). Must be non-empty.</param>
        /// <param name="startsOnDate">Inclusive start date for this segment.</param>
        /// <param name="endsBeforeDate">Exclusive end date; null for never/AfterCount.</param>
        /// <param name="title">Task title template. Must be non-empty.</param>
        /// <param name="description">Optional description template.</param>
        /// <param name="startTime">Optional start time template.</param>
        /// <param name="endTime">Optional end time template.</param>
        /// <param name="location">Optional location template.</param>
        /// <param name="travelTime">Optional travel time template.</param>
        /// <param name="categoryId">Optional category template.</param>
        /// <param name="priority">Priority template.</param>
        /// <param name="meetingLink">Optional meeting link template.</param>
        /// <param name="reminderOffsetMinutes">Minutes before StartTime for reminders; null = no reminder.</param>
        /// <param name="materializedUpToDate">Starting materialization horizon (usually StartsOnDate minus 1 day).</param>
        /// <param name="utcNow">Current UTC time.</param>
        public static DomainResult<RecurringTaskSeries> Create(Guid userId,
                                                               Guid rootId,
                                                               string rruleString,
                                                               DateOnly startsOnDate,
                                                               DateOnly? endsBeforeDate,
                                                               string? title,
                                                               string? description,
                                                               TimeOnly? startTime,
                                                               TimeOnly? endTime,
                                                               string? location,
                                                               TimeSpan? travelTime,
                                                               Guid? categoryId,
                                                               TaskPriority priority,
                                                               string? meetingLink,
                                                               int? reminderOffsetMinutes,
                                                               DateOnly materializedUpToDate,
                                                               DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;
            var normalizedDescription = description?.Trim();
            var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
            var normalizedMeetingLink = string.IsNullOrWhiteSpace(meetingLink) ? null : meetingLink.Trim();
            var normalizedRRule = rruleString?.Trim() ?? string.Empty;

            if (userId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSeries.UserId.Empty", "UserId must be a non-empty GUID."));
            }

            if (rootId == Guid.Empty)
            {
                errors.Add(new DomainError("RecurringSeries.RootId.Empty", "RootId must be a non-empty GUID."));
            }

            if (normalizedRRule.Length == 0)
            {
                errors.Add(new DomainError("RecurringSeries.RRuleString.Empty", "RRuleString must not be empty."));
            }

            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("RecurringSeries.Title.Empty", "Title must not be empty."));
            }

            if (startsOnDate == default)
            {
                errors.Add(new DomainError("RecurringSeries.StartsOnDate.Default", "StartsOnDate must be a valid calendar date."));
            }

            if (startTime.HasValue && endTime.HasValue && endTime < startTime)
            {
                errors.Add(new DomainError("RecurringSeries.Time.Invalid", "EndTime cannot be earlier than StartTime."));
            }

            if (endsBeforeDate.HasValue && startsOnDate != default && endsBeforeDate.Value <= startsOnDate)
            {
                errors.Add(new DomainError("RecurringSeries.EndsBeforeDate.Invalid", "EndsBeforeDate must be after StartsOnDate."));
            }

            if (errors.Count > 0)
            {
                return DomainResult<RecurringTaskSeries>.Failure(errors);
            }

            var id = Guid.NewGuid();
            return DomainResult<RecurringTaskSeries>.Success(
                new RecurringTaskSeries(id,
                                        userId,
                                        rootId,
                                        normalizedRRule,
                                        startsOnDate,
                                        endsBeforeDate,
                                        normalizedTitle,
                                        normalizedDescription,
                                        startTime,
                                        endTime,
                                        normalizedLocation,
                                        travelTime,
                                        categoryId,
                                        priority,
                                        normalizedMeetingLink,
                                        reminderOffsetMinutes,
                                        materializedUpToDate,
                                        utcNow));
        }

        // BEHAVIOURS

        /// <summary>
        /// Updates all template task fields for this series segment.
        /// Does not change recurrence rule, start date, or end condition.
        /// </summary>
        public DomainResult UpdateTemplate(string? title,
                                           string? description,
                                           TimeOnly? startTime,
                                           TimeOnly? endTime,
                                           string? location,
                                           TimeSpan? travelTime,
                                           Guid? categoryId,
                                           TaskPriority priority,
                                           string? meetingLink,
                                           int? reminderOffsetMinutes,
                                           DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedTitle = title?.Trim() ?? string.Empty;
            var normalizedDescription = description?.Trim();
            var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
            var normalizedMeetingLink = string.IsNullOrWhiteSpace(meetingLink) ? null : meetingLink.Trim();

            if (normalizedTitle.Length == 0)
            {
                errors.Add(new DomainError("RecurringSeries.Title.Empty", "Title must not be empty."));
            }

            if (startTime.HasValue && endTime.HasValue && endTime < startTime)
            {
                errors.Add(new DomainError("RecurringSeries.Time.Invalid", "EndTime cannot be earlier than StartTime."));
            }

            if (IsDeleted)
            {
                errors.Add(new DomainError("RecurringSeries.Deleted", "Cannot update a deleted series."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            Title = normalizedTitle;
            Description = normalizedDescription;
            StartTime = startTime;
            EndTime = endTime;
            Location = normalizedLocation;
            TravelTime = travelTime;
            CategoryId = categoryId;
            Priority = priority;
            MeetingLink = normalizedMeetingLink;
            ReminderOffsetMinutes = reminderOffsetMinutes;

            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Replaces the recurrence rule string.
        /// Only valid when the series has not been deleted.
        /// </summary>
        public DomainResult UpdateRRuleString(string rruleString, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            var normalizedRRule = rruleString?.Trim() ?? string.Empty;

            if (normalizedRRule.Length == 0)
            {
                errors.Add(new DomainError("RecurringSeries.RRuleString.Empty", "RRuleString must not be empty."));
            }

            if (IsDeleted)
            {
                errors.Add(new DomainError("RecurringSeries.Deleted", "Cannot update a deleted series."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            RRuleString = normalizedRRule;
            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Advances the materialization horizon to the given date.
        /// Only moves the horizon forward — calling with a date earlier than the current
        /// MaterializedUpToDate is a no-op.
        /// </summary>
        public DomainResult AdvanceMaterializedHorizon(DateOnly newDate, DateTime utcNow)
        {
            if (IsDeleted)
            {
                return DomainResult.Failure(
                    new DomainError("RecurringSeries.Deleted", "Cannot advance horizon on a deleted series."));
            }

            if (newDate <= MaterializedUpToDate)
            {
                // Already materialized up to or past this date — idempotent no-op.
                return DomainResult.Success();
            }

            MaterializedUpToDate = newDate;
            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Terminates this series segment by setting an exclusive upper bound on EndsBeforeDate.
        /// Used when a "this and following" edit splits the series — the old segment is terminated
        /// at the split date and a new segment begins from that date.
        /// </summary>
        public DomainResult Terminate(DateOnly endsBeforeDate, DateTime utcNow)
        {
            var errors = new List<DomainError>();

            if (IsDeleted)
            {
                errors.Add(new DomainError("RecurringSeries.Deleted", "Cannot terminate a deleted series."));
            }

            if (endsBeforeDate <= StartsOnDate)
            {
                errors.Add(new DomainError("RecurringSeries.Terminate.Invalid",
                    "EndsBeforeDate must be after StartsOnDate."));
            }

            if (errors.Count > 0)
            {
                return DomainResult.Failure(errors);
            }

            EndsBeforeDate = endsBeforeDate;
            IncrementVersion();
            Touch(utcNow);
            return DomainResult.Success();
        }

        /// <summary>
        /// Soft-deletes the series. Idempotent — calling on an already-deleted series is a no-op.
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
