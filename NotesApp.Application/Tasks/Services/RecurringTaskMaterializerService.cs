using FluentResults;
using NotesApp.Application.Abstractions;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotesApp.Application.Tasks.Services
{
    /// <summary>
    /// Default implementation of <see cref="IRecurringTaskMaterializerService"/>.
    ///
    /// Pure application-layer logic — no EF, no IO. Converts recurrence engine output
    /// into concrete TaskItem + Subtask pairs ready for the caller to persist.
    /// </summary>
    public sealed class RecurringTaskMaterializerService : IRecurringTaskMaterializerService
    {
        private readonly IRecurrenceEngine _recurrenceEngine;

        public RecurringTaskMaterializerService(IRecurrenceEngine recurrenceEngine)
        {
            _recurrenceEngine = recurrenceEngine;
        }

        /// <inheritdoc />
        public Result<MaterializationBatch> MaterializeInitialBatch(RecurringTaskSeries series,
                                                                    IReadOnlyList<RecurringTaskSubtask> templateSubtasks,
                                                                    IReadOnlyList<RecurringTaskException> exceptions,
                                                                    IReadOnlyDictionary<Guid, IReadOnlyList<RecurringTaskSubtask>> exceptionSubtasksById,
                                                                    DateTime utcNow,
                                                                    int batchSize)
        {
            var toExclusive = DateOnly.FromDateTime(utcNow).AddDays(1); // today + 1 (generates up to today)
            return MaterializeRange(series,
                                    templateSubtasks,
                                    exceptions,
                                    exceptionSubtasksById,
                                    fromInclusive: series.StartsOnDate,
                                    toExclusive: toExclusive,
                                    utcNow,
                                    maxBatch: batchSize);
        }

        /// <inheritdoc />
        public Result<MaterializationBatch> AdvanceHorizon(RecurringTaskSeries series,
                                                           IReadOnlyList<RecurringTaskSubtask> templateSubtasks,
                                                           IReadOnlyList<RecurringTaskException> exceptions,
                                                           IReadOnlyDictionary<Guid, IReadOnlyList<RecurringTaskSubtask>> exceptionSubtasksById,
                                                           DateOnly targetDate,
                                                           DateTime utcNow)
        {
            // Advance from one day past the current horizon up to and including targetDate.
            var fromInclusive = series.MaterializedUpToDate.AddDays(1);
            var toExclusive = targetDate.AddDays(1); // GenerateOccurrences uses exclusive upper bound
            return MaterializeRange(series,
                                    templateSubtasks,
                                    exceptions,
                                    exceptionSubtasksById,
                                    fromInclusive,
                                    toExclusive,
                                    utcNow,
                                    maxBatch: int.MaxValue);
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private Result<MaterializationBatch> MaterializeRange(RecurringTaskSeries series,
                                                              IReadOnlyList<RecurringTaskSubtask> templateSubtasks,
                                                              IReadOnlyList<RecurringTaskException> exceptions,
                                                              IReadOnlyDictionary<Guid, IReadOnlyList<RecurringTaskSubtask>> exceptionSubtasksById,
                                                              DateOnly fromInclusive,
                                                              DateOnly toExclusive,
                                                              DateTime utcNow,
                                                              int maxBatch)
        {
            // Build fast-lookup structures for exceptions
            var deletionDates = new HashSet<DateOnly>();
            var overridesByDate = new Dictionary<DateOnly, RecurringTaskException>();
            foreach (var ex in exceptions)
            {
                if (ex.IsDeletion)
                    deletionDates.Add(ex.OccurrenceDate);
                else
                    overridesByDate[ex.OccurrenceDate] = ex;
            }

            var tasks = new List<TaskItem>();
            var subtasks = new List<Subtask>();
            var count = 0;

            var occurrenceDates = _recurrenceEngine.GenerateOccurrences(series.RRuleString,
                                                                        series.StartsOnDate,
                                                                        series.EndsBeforeDate,
                                                                        fromInclusive,
                                                                        toExclusive);

            foreach (var canonicalDate in occurrenceDates)
            {
                if (count >= maxBatch) break;

                // Skip deleted occurrences.
                if (deletionDates.Contains(canonicalDate)) continue;

                // Resolve field values: exception overrides take priority over series template.
                overridesByDate.TryGetValue(canonicalDate, out var exception);

                var date        = exception?.OverrideDate        ?? canonicalDate;
                var title       = exception?.OverrideTitle       ?? series.Title;
                var description = exception?.OverrideDescription ?? series.Description;
                var startTime   = exception?.OverrideStartTime   ?? series.StartTime;
                var endTime     = exception?.OverrideEndTime     ?? series.EndTime;
                var location    = exception?.OverrideLocation    ?? series.Location;
                var travelTime  = exception?.OverrideTravelTime  ?? series.TravelTime;
                var categoryId  = exception?.OverrideCategoryId  ?? series.CategoryId;
                var priority    = exception?.OverridePriority    ?? series.Priority;
                var meetingLink = exception?.OverrideMeetingLink ?? series.MeetingLink;

                // Compute reminder UTC from series offset + resolved date + startTime.
                DateTime? reminderAtUtc = ComputeReminderUtc(exception?.OverrideReminderAtUtc,
                                                             series.ReminderOffsetMinutes,
                                                             date,
                                                             startTime ?? series.StartTime);

                var createResult = TaskItem.Create(userId:      series.UserId,
                                                   date:        date,
                                                   title:       title,
                                                   description: description,
                                                   startTime:   startTime,
                                                   endTime:     endTime,
                                                   location:    location,
                                                   travelTime:  travelTime,
                                                   categoryId:  categoryId,
                                                   priority:    priority,
                                                   utcNow:      utcNow,
                                                   meetingLink: meetingLink);

                if (createResult.IsFailure)
                {
                    var messages = string.Join("; ", createResult.Errors.Select(e => e.Message));
                    return Result.Fail<MaterializationBatch>(
                        new Error($"Failed to create TaskItem for occurrence on {canonicalDate:yyyy-MM-dd} " +
                                  $"in series {series.Id}: {messages}")
                            .WithMetadata("ErrorCode", "Materializer.Task.CreateFailed")
                            .WithMetadata("SeriesId", series.Id)
                            .WithMetadata("OccurrenceDate", canonicalDate));
                }

                var task = createResult.Value;
                task.LinkToSeries(series.Id, canonicalDate);

                if (reminderAtUtc.HasValue)
                {
                    var reminderResult = task.SetReminder(reminderAtUtc, utcNow);
                    if (reminderResult.IsFailure)
                    {
                        // SetReminder only fails when the entity is deleted, which cannot happen
                        // on a freshly-created TaskItem. Treat as an unexpected domain invariant
                        // violation and surface it rather than silently dropping the occurrence.
                        var messages = string.Join("; ", reminderResult.Errors.Select(e => e.Message));
                        return Result.Fail<MaterializationBatch>(
                            new Error($"Failed to set reminder for occurrence on {canonicalDate:yyyy-MM-dd} " +
                                      $"in series {series.Id}: {messages}")
                                .WithMetadata("ErrorCode", "Materializer.Task.SetReminderFailed")
                                .WithMetadata("SeriesId", series.Id)
                                .WithMetadata("OccurrenceDate", canonicalDate));
                    }
                }

                tasks.Add(task);
                count++;

                // Determine subtask source:
                // Exception existence (not row count) is the signal for an override.
                //   Exception exists → use its subtask rows even if empty.
                //   Empty = occurrence was explicitly cleared to have no subtasks.
                //   No exception → copy from the series template subtasks.
                //
                // Exceptions with zero subtask rows produce no key in the dictionary —
                // TryGetValue returns false and we fall back to an empty list (not the template),
                // preserving the "explicitly cleared" intent.
                IReadOnlyList<RecurringTaskSubtask> subtaskSource;
                if (exception != null)
                {
                    exceptionSubtasksById.TryGetValue(exception.Id, out var exSubtasks);
                    subtaskSource = exSubtasks ?? (IReadOnlyList<RecurringTaskSubtask>)[];
                }
                else
                {
                    subtaskSource = templateSubtasks;
                }

                foreach (var st in subtaskSource)
                {
                    var subtaskResult = Subtask.Create(userId:   series.UserId,
                                                       taskId:   task.Id,
                                                       text:     st.Text,
                                                       position: st.Position,
                                                       utcNow:   utcNow);

                    if (subtaskResult.IsFailure)
                    {
                        var messages = string.Join("; ", subtaskResult.Errors.Select(e => e.Message));
                        return Result.Fail<MaterializationBatch>(
                            new Error($"Failed to create subtask '{st.Text}' for occurrence on " +
                                      $"{canonicalDate:yyyy-MM-dd} in series {series.Id}: {messages}")
                                .WithMetadata("ErrorCode", "Materializer.Subtask.CreateFailed")
                                .WithMetadata("SeriesId", series.Id)
                                .WithMetadata("OccurrenceDate", canonicalDate));
                    }

                    var subtask = subtaskResult.Value;

                    // For exception subtasks, preserve the IsCompleted state.
                    // SetCompleted only fails when the entity is deleted, which cannot happen
                    // on a freshly-created Subtask — surface any failure rather than silently dropping.
                    if (st.IsCompleted)
                    {
                        var completedResult = subtask.SetCompleted(true, utcNow);
                        if (completedResult.IsFailure)
                        {
                            var messages = string.Join("; ", completedResult.Errors.Select(e => e.Message));
                            return Result.Fail<MaterializationBatch>(
                                new Error($"Failed to mark subtask '{st.Text}' as completed for occurrence on " +
                                          $"{canonicalDate:yyyy-MM-dd} in series {series.Id}: {messages}")
                                    .WithMetadata("ErrorCode", "Materializer.Subtask.SetCompletedFailed")
                                    .WithMetadata("SeriesId", series.Id)
                                    .WithMetadata("OccurrenceDate", canonicalDate));
                        }
                    }

                    subtasks.Add(subtask);
                }
            }

            return Result.Ok(new MaterializationBatch(tasks, subtasks));
        }

        // Reminder computation is delegated to RecurringReminderHelper so the same
        // logic is shared with GetVirtualTaskOccurrenceDetailQueryHandler and TaskRepository.
        private static DateTime? ComputeReminderUtc(DateTime? overrideReminderAtUtc,
                                                    int? reminderOffsetMinutes,
                                                    DateOnly occurrenceDate,
                                                    TimeOnly? startTime)
            => RecurringReminderHelper.ComputeReminderUtc(
                overrideReminderAtUtc, reminderOffsetMinutes, occurrenceDate, startTime);
    }
}
