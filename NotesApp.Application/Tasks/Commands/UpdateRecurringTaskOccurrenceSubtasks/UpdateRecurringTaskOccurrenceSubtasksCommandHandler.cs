using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Application.Tasks.Services;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrenceSubtasks
{
    /// <summary>
    /// Handles <see cref="UpdateRecurringTaskOccurrenceSubtasksCommand"/>.
    ///
    /// Single scope — materialized (<see cref="UpdateRecurringTaskOccurrenceSubtasksCommand.TaskItemId"/> provided):
    /// 1. Load TaskItem by TaskItemId and verify ownership.
    /// 2. SoftDeleteAllForTaskAsync — stages soft-deletes for all existing Subtask rows (change-tracker).
    /// 3. Create new Subtask rows from the desired list (staged).
    /// 4. Single SaveChangesAsync() — commits old soft-deletes + new rows atomically.
    ///
    /// Single scope — virtual (TaskItemId null, Decision #10 from the plan):
    /// 1. Pre-check for an existing RecurringTaskException for (SeriesId, OccurrenceDate).
    /// 2. If none: load series → copy all template fields as explicit overrides into a new exception
    ///    (isolates this occurrence from future "edit all" changes).
    /// 3. If exists (and is not a deletion): use the existing exception.
    /// 4. Load current exception subtask rows (RecurringTaskSubtask with ExceptionId set).
    /// 5. Soft-delete each current exception subtask (change-tracker).
    /// 6. Create new RecurringTaskSubtask rows (ExceptionId set) for each desired subtask.
    /// 7. Single SaveChangesAsync().
    ///
    /// ThisAndFollowing scope:
    /// 1. Load old series (untracked).
    /// 2. Bulk soft-delete materialized TaskItems from OccurrenceDate forward (change-tracker).
    /// 3. Bulk soft-delete exceptions from OccurrenceDate forward (change-tracker).
    /// 4. Terminate old series at OccurrenceDate.
    ///    Old series template subtasks are left intact — pre-split virtual occurrences still inherit them.
    /// 5. Create new series segment (inherits all template fields from old series).
    /// 6. Create new RecurringTaskSubtask rows (SeriesId set) for the new series.
    /// 7. Materialize initial batch for the new series (only up to the active horizon).
    /// 8. Single SaveChangesAsync().
    ///
    /// All scope:
    /// 1. Load reference series to obtain RootId.
    /// 2. GetActiveByRootIdAsync to get all active series segments for the root.
    /// 3. For each series: soft-delete old template subtasks (change-tracker) → create new template subtasks.
    /// 4. For each series: load materialized tasks → skip individually-modified ones → for the rest:
    ///    SoftDeleteAllForTaskAsync (change-tracker) + create new Subtask rows (staged).
    /// 5. Single SaveChangesAsync() — commits template subtask changes + old soft-deletes + new Subtask rows
    ///    fully atomically.
    /// </summary>
    public sealed class UpdateRecurringTaskOccurrenceSubtasksCommandHandler
        : IRequestHandler<UpdateRecurringTaskOccurrenceSubtasksCommand, Result>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        // REFACTORED: added for recurring-task-attachments feature
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly IRecurringTaskMaterializerService _materializerService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly RecurringTaskOptions _recurringOptions;

        public UpdateRecurringTaskOccurrenceSubtasksCommandHandler(
            ITaskRepository taskRepository,
            ISubtaskRepository subtaskRepository,
            IRecurringTaskSeriesRepository seriesRepository,
            IRecurringTaskExceptionRepository exceptionRepository,
            IRecurringTaskSubtaskRepository recurringSubtaskRepository,
            IRecurringTaskAttachmentRepository recurringAttachmentRepository,
            IRecurringTaskMaterializerService materializerService,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            IOptions<RecurringTaskOptions> recurringOptions)
        {
            _taskRepository = taskRepository;
            _subtaskRepository = subtaskRepository;
            _seriesRepository = seriesRepository;
            _exceptionRepository = exceptionRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository;
            _materializerService = materializerService;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _recurringOptions = recurringOptions.Value;
        }

        public async Task<Result> Handle(UpdateRecurringTaskOccurrenceSubtasksCommand command,
                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            return command.Scope switch
            {
                RecurringEditScope.Single => await HandleSingleAsync(command, userId, utcNow, cancellationToken),
                RecurringEditScope.ThisAndFollowing => await HandleThisAndFollowingAsync(command, userId, utcNow, cancellationToken),
                RecurringEditScope.All => await HandleAllAsync(command, userId, utcNow, cancellationToken),
                _ => Result.Fail($"Unknown RecurringEditScope: {command.Scope}")
            };
        }

        // -------------------------------------------------------------------------
        // Single scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleSingleAsync(
            UpdateRecurringTaskOccurrenceSubtasksCommand command,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            if (command.TaskItemId.HasValue)
            {
                return await HandleSingleMaterializedAsync(command, userId, utcNow, cancellationToken);
            }

            return await HandleSingleVirtualAsync(command, userId, utcNow, cancellationToken);
        }

        /// <summary>
        /// Single scope — materialized occurrence.
        /// Replaces Subtask rows directly on the TaskItem.
        /// No exception created — the TaskItem itself is the concrete record.
        /// </summary>
        private async Task<Result> HandleSingleMaterializedAsync(UpdateRecurringTaskOccurrenceSubtasksCommand command,
                                                                 Guid userId,
                                                                 DateTime utcNow,
                                                                 CancellationToken cancellationToken)
        {
            var task = await _taskRepository.GetByIdUntrackedAsync(command.TaskItemId!.Value, cancellationToken);

            if (task is null || task.UserId != userId)
            {
                return Result.Fail(
                    new Error("Task not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "Tasks.NotFound"));
            }

            if (!task.RecurringSeriesId.HasValue)
            {
                return Result.Fail(
                    new Error("Task is not part of a recurring series.")
                        .WithMetadata("ErrorCode", "Tasks.NotRecurring"));
            }

            // Soft-delete all existing Subtask rows (change-tracker — staged, not yet committed).
            await _subtaskRepository.SoftDeleteAllForTaskAsync(
                task.Id, userId, utcNow, cancellationToken);

            // Create and stage new Subtask rows.
            foreach (var desired in command.Subtasks)
            {
                var subtaskResult = Subtask.Create(
                    userId: userId,
                    taskId: task.Id,
                    text: desired.Text,
                    position: desired.Position,
                    utcNow: utcNow);

                if (subtaskResult.IsFailure)
                {
                    return Result.Fail(subtaskResult.Errors.Select(e => new Error(e.Message)));
                }

                var subtask = subtaskResult.Value;

                // Preserve the completion state the client sent.
                // Subtask.Create always initialises IsCompleted = false, so we only
                // need to call SetCompleted when the desired state is true.
                if (desired.IsCompleted)
                {
                    var completedResult = subtask.SetCompleted(true, utcNow);
                    if (completedResult.IsFailure)
                    {
                        return Result.Fail(completedResult.Errors.Select(e => new Error(e.Message)));
                    }
                }

                await _subtaskRepository.AddAsync(subtask, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        /// <summary>
        /// Single scope — virtual occurrence.
        /// Creates or updates a RecurringTaskException and replaces its RecurringTaskSubtask rows.
        /// </summary>
        private async Task<Result> HandleSingleVirtualAsync(UpdateRecurringTaskOccurrenceSubtasksCommand command,
                                                            Guid userId,
                                                            DateTime utcNow,
                                                            CancellationToken cancellationToken)
        {
            // 1. Pre-check for an existing exception.
            var exception = await _exceptionRepository.GetByOccurrenceAsync(
                command.SeriesId, command.OccurrenceDate, cancellationToken);

            // Deletion exceptions cannot have subtask overrides.
            if (exception is not null && exception.IsDeletion)
            {
                return Result.Fail(
                    new Error("Cannot set subtasks on a deleted occurrence.")
                        .WithMetadata("ErrorCode", "RecurringOccurrence.Deleted"));
            }

            if (exception is null)
            {
                // 2. No exception exists — create one by copying all series template fields.
                //    This isolates this occurrence from future "edit all" template changes.
                var series = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

                if (series is null || series.UserId != userId)
                {
                    return Result.Fail(
                        new Error("Recurring series not found or does not belong to you.")
                            .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
                }

                var exResult = RecurringTaskException.CreateOverride(
                    userId: userId,
                    seriesId: command.SeriesId,
                    occurrenceDate: command.OccurrenceDate,
                    overrideTitle: series.Title,
                    overrideDescription: series.Description,
                    overrideDate: null,
                    overrideStartTime: series.StartTime,
                    overrideEndTime: series.EndTime,
                    overrideLocation: series.Location,
                    overrideTravelTime: series.TravelTime,
                    overrideCategoryId: series.CategoryId,
                    overridePriority: series.Priority,
                    overrideMeetingLink: series.MeetingLink,
                    overrideReminderAtUtc: null,    // reminder uses offset; not stored as absolute UTC here
                    isCompleted: false,             // no exception existed before this call → occurrence was not completed
                    materializedTaskItemId: null,
                    utcNow: utcNow);

                if (exResult.IsFailure)
                {
                    return Result.Fail(exResult.Errors.Select(e => new Error(e.Message)));
                }

                exception = exResult.Value;
                await _exceptionRepository.AddAsync(exception, cancellationToken);
            }

            // 3. Load current exception subtask rows and soft-delete them (full replace semantics).
            var existingSubtasks = await _recurringSubtaskRepository.GetByExceptionIdAsync(
                exception.Id, cancellationToken);

            foreach (var st in existingSubtasks)
            {
                var softDeleteResult = st.SoftDelete(utcNow);
                if (softDeleteResult.IsFailure)
                {
                    return Result.Fail(softDeleteResult.Errors.Select(e => new Error(e.Message)));
                }

                _recurringSubtaskRepository.Update(st);
            }

            // 4. Create new RecurringTaskSubtask rows for each desired subtask.
            foreach (var desired in command.Subtasks)
            {
                var stResult = RecurringTaskSubtask.CreateForException(
                    userId: userId,
                    exceptionId: exception.Id,
                    text: desired.Text,
                    position: desired.Position,
                    isCompleted: desired.IsCompleted,
                    utcNow: utcNow);

                if (stResult.IsFailure)
                {
                    return Result.Fail(stResult.Errors.Select(e => new Error(e.Message)));
                }

                await _recurringSubtaskRepository.AddAsync(stResult.Value, cancellationToken);
            }

            // 5. Persist atomically:
            //    - New or updated exception row
            //    - Soft-deleted existing exception subtasks (staged via Update)
            //    - New exception subtask rows (staged via AddAsync)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        // -------------------------------------------------------------------------
        // ThisAndFollowing scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleThisAndFollowingAsync(UpdateRecurringTaskOccurrenceSubtasksCommand command,
                                                               Guid userId,
                                                               DateTime utcNow,
                                                               CancellationToken cancellationToken)
        {
            var today = DateOnly.FromDateTime(utcNow);
            var currentHorizon = today.AddDays(_recurringOptions.HorizonWeeksAhead * 7);

            // 1. Load the old series (untracked) — we need its template fields and RootId.
            var oldSeries = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (oldSeries is null || oldSeries.UserId != userId)
            {
                return Result.Fail(
                    new Error("Recurring series not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            // Capture the original end date before Terminate() overwrites it.
            // The new series inherits the same natural end condition as the old series.
            var originalEndsBeforeDate = oldSeries.EndsBeforeDate;

            // 2. Bulk soft-delete all materialized TaskItems from OccurrenceDate forward (change-tracker).
            await _taskRepository.SoftDeleteRecurringFromDateAsync(
                command.SeriesId, command.OccurrenceDate, userId, utcNow, cancellationToken);

            // 3. Bulk soft-delete all exceptions from OccurrenceDate forward (change-tracker).
            await _exceptionRepository.SoftDeleteFromDateAsync(
                command.SeriesId, command.OccurrenceDate, userId, utcNow, cancellationToken);

            // 4. Terminate the old series at OccurrenceDate (sets EndsBeforeDate = OccurrenceDate).
            var terminateResult = oldSeries.Terminate(command.OccurrenceDate, utcNow);
            if (terminateResult.IsFailure)
            {
                return Result.Fail(terminateResult.Errors.Select(e => new Error(e.Message)));
            }
            _seriesRepository.Update(oldSeries);

            // Note: old series template subtasks are intentionally left intact.
            // Virtual occurrences that fall before OccurrenceDate still belong to the old series
            // and must continue to render with its original subtask template.
            // Only the new series (from OccurrenceDate forward) receives the new subtask list.

            // 5. Create a new series segment inheriting all template fields from the old series.
            //    Only the subtask list changes — title, times, recurrence rule, etc. are carried forward.
            var newSeriesResult = RecurringTaskSeries.Create(userId: userId,
                                                             rootId: oldSeries.RootId,
                                                             rruleString: oldSeries.RRuleString,
                                                             startsOnDate: command.OccurrenceDate,
                                                             endsBeforeDate: originalEndsBeforeDate,
                                                             title: oldSeries.Title,
                                                             description: oldSeries.Description,
                                                             startTime: oldSeries.StartTime,
                                                             endTime: oldSeries.EndTime,
                                                             location: oldSeries.Location,
                                                             travelTime: oldSeries.TravelTime,
                                                             categoryId: oldSeries.CategoryId,
                                                             priority: oldSeries.Priority,
                                                             meetingLink: oldSeries.MeetingLink,
                                                             reminderOffsetMinutes: oldSeries.ReminderOffsetMinutes,
                                                             materializedUpToDate: command.OccurrenceDate.AddDays(-1),
                                                             utcNow: utcNow);

            if (newSeriesResult.IsFailure)
            {
                return Result.Fail(newSeriesResult.Errors.Select(e => new Error(e.Message)));
            }

            var newSeries = newSeriesResult.Value;

            // 6. Create new template subtasks for the new series.
            var newTemplateSubtasks = new List<RecurringTaskSubtask>();
            foreach (var desired in command.Subtasks)
            {
                var stResult = RecurringTaskSubtask.CreateForSeries(
                    userId: userId,
                    seriesId: newSeries.Id,
                    text: desired.Text,
                    position: desired.Position,
                    utcNow: utcNow);

                if (stResult.IsFailure)
                {
                    return Result.Fail(stResult.Errors.Select(e => new Error(e.Message)));
                }

                newTemplateSubtasks.Add(stResult.Value);
            }

            // 7. Materialize the initial batch for the new series.
            //    Only materialize within the active horizon (Decision #9 from the plan).
            MaterializationBatch batch;
            if (command.OccurrenceDate <= currentHorizon)
            {
                var batchResult = _materializerService.MaterializeInitialBatch(
                    series: newSeries,
                    templateSubtasks: newTemplateSubtasks,
                    exceptions: [],
                    exceptionSubtasksById: new Dictionary<Guid, IReadOnlyList<RecurringTaskSubtask>>(),
                    utcNow: utcNow,
                    batchSize: _recurringOptions.InitialMaterializationBatchSize);

                if (batchResult.IsFailed)
                    return batchResult.ToResult();

                batch = batchResult.Value;

                var advanceResult = newSeries.AdvanceMaterializedHorizon(today, utcNow);
                if (advanceResult.IsFailure)
                {
                    return Result.Fail(advanceResult.Errors.Select(e => new Error(e.Message)));
                }
            }
            else
            {
                // Future start — let the horizon worker handle materialization.
                batch = new MaterializationBatch([], []);
            }

            // 8. Persist everything in a single SaveChangesAsync:
            //    - Soft-deleted TaskItems (change-tracker from step 2)
            //    - Soft-deleted exceptions (change-tracker from step 3)
            //    - Terminated old series (staged via Update in step 4)
            //    - New series (step 5)
            //    - New template subtasks (step 6)
            //    - New materialized TaskItems + Subtask rows (step 7)
            //    - Copied series template attachments (below)
            await _seriesRepository.AddAsync(newSeries, cancellationToken);

            foreach (var st in newTemplateSubtasks)
            {
                await _recurringSubtaskRepository.AddAsync(st, cancellationToken);
            }

            // Copy series template attachments from old series to new series (same BlobPath, new row IDs).
            var oldSeriesAttachments = await _recurringAttachmentRepository.GetBySeriesIdAsync(
                oldSeries.Id, cancellationToken);

            foreach (var template in oldSeriesAttachments)
            {
                var copyResult = RecurringTaskAttachment.CreateForSeries(
                    id: Guid.NewGuid(),
                    userId: userId,
                    seriesId: newSeries.Id,
                    fileName: template.FileName,
                    contentType: template.ContentType,
                    sizeBytes: template.SizeBytes,
                    blobPath: template.BlobPath,
                    displayOrder: template.DisplayOrder,
                    utcNow: utcNow);

                if (copyResult.IsFailure)
                    return Result.Fail(copyResult.Errors.Select(e => new Error(e.Message)));

                await _recurringAttachmentRepository.AddAsync(copyResult.Value!, cancellationToken);
            }

            foreach (var task in batch.Tasks)
            {
                await _taskRepository.AddAsync(task, cancellationToken);
            }

            foreach (var subtask in batch.Subtasks)
            {
                await _subtaskRepository.AddAsync(subtask, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }

        // -------------------------------------------------------------------------
        // All scope
        // -------------------------------------------------------------------------

        private async Task<Result> HandleAllAsync(
            UpdateRecurringTaskOccurrenceSubtasksCommand command,
            Guid userId,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            // 1. Resolve the reference series to get the RootId.
            var referenceSeries = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (referenceSeries is null || referenceSeries.UserId != userId)
            {
                return Result.Fail(
                    new Error("Recurring series not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            var rootId = referenceSeries.RootId;

            // 2. Load all active series segments for the root.
            var allSeries = await _seriesRepository.GetActiveByRootIdAsync(rootId, userId, cancellationToken);

            foreach (var series in allSeries)
            {
                // 3. Soft-delete old template subtasks for this series (change-tracker).
                var oldTemplateSubtasks = await _recurringSubtaskRepository.GetBySeriesIdAsync(
                    series.Id, cancellationToken);

                foreach (var st in oldTemplateSubtasks)
                {
                    var softDeleteResult = st.SoftDelete(utcNow);
                    if (softDeleteResult.IsFailure)
                    {
                        return Result.Fail(softDeleteResult.Errors.Select(e => new Error(e.Message)));
                    }

                    _recurringSubtaskRepository.Update(st);
                }

                // 4. Create new template subtasks for this series.
                foreach (var desired in command.Subtasks)
                {
                    var stResult = RecurringTaskSubtask.CreateForSeries(
                        userId: userId,
                        seriesId: series.Id,
                        text: desired.Text,
                        position: desired.Position,
                        utcNow: utcNow);

                    if (stResult.IsFailure)
                    {
                        return Result.Fail(stResult.Errors.Select(e => new Error(e.Message)));
                    }

                    await _recurringSubtaskRepository.AddAsync(stResult.Value, cancellationToken);
                }

                // 5. Load materialized tasks for this series.
                var tasks = await _taskRepository.GetBySeriesAsync(series.Id, userId, cancellationToken);

                if (tasks.Count == 0) continue;

                // 6. Identify individually-modified occurrences (non-deletion exceptions that have a
                //    MaterializedTaskItemId). These are preserved — only template-inherited tasks are updated.
                var exceptions = await _exceptionRepository.GetForSeriesInRangeAsync(
                    series.Id, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);

                var individuallyModifiedDates = new HashSet<DateOnly>(
                    exceptions
                        .Where(e => !e.IsDeletion && e.MaterializedTaskItemId.HasValue)
                        .Select(e => e.OccurrenceDate));

                // 7. Replace subtask rows on each task that has no individual exception.
                foreach (var task in tasks)
                {
                    if (task.CanonicalOccurrenceDate.HasValue &&
                        individuallyModifiedDates.Contains(task.CanonicalOccurrenceDate.Value))
                    {
                        continue; // Preserve individually-modified occurrence.
                    }

                    // Soft-delete all existing Subtask rows for this task (change-tracker — staged).
                    await _subtaskRepository.SoftDeleteAllForTaskAsync(
                        task.Id, userId, utcNow, cancellationToken);

                    // Create new Subtask rows from the desired list.
                    foreach (var desired in command.Subtasks)
                    {
                        var subtaskResult = Subtask.Create(
                            userId: userId,
                            taskId: task.Id,
                            text: desired.Text,
                            position: desired.Position,
                            utcNow: utcNow);

                        if (subtaskResult.IsFailure)
                        {
                            return Result.Fail(subtaskResult.Errors.Select(e => new Error(e.Message)));
                        }

                        var subtask = subtaskResult.Value;

                        // Preserve the completion state the client sent.
                        if (desired.IsCompleted)
                        {
                            var completedResult = subtask.SetCompleted(true, utcNow);
                            if (completedResult.IsFailure)
                            {
                                return Result.Fail(completedResult.Errors.Select(e => new Error(e.Message)));
                            }
                        }

                        await _subtaskRepository.AddAsync(subtask, cancellationToken);
                    }
                }
            }

            // 8. Persist all staged changes atomically in one SaveChangesAsync:
            //    - Old template subtask soft-deletes (change-tracker, step 3)
            //    - New template subtask rows (step 4)
            //    - Old materialized Subtask soft-deletes (change-tracker, step 7)
            //    - New materialized Subtask rows (step 7)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Ok();
        }
    }
}
