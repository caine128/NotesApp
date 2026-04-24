using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Application.Tasks;
using NotesApp.Application.Tasks.Models;
using NotesApp.Application.Tasks.Services;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Application.Tasks.Commands.UpdateRecurringTaskOccurrence
{
    /// <summary>
    /// Handles <see cref="UpdateRecurringTaskOccurrenceCommand"/>.
    ///
    /// Single scope       — upserts a RecurringTaskException with override fields; also updates
    ///                      the materialized TaskItem when TaskItemId is provided.
    /// ThisAndFollowing   — terminates the current series, creates a new series with new template
    ///                      fields starting from OccurrenceDate, and materializes the initial batch.
    /// All                — updates the template fields of every active series segment for the root;
    ///                      also updates materialized TaskItems that have no individual exception.
    ///
    /// All scope operations commit in a single SaveChangesAsync() call.
    /// </summary>
    public sealed class UpdateRecurringTaskOccurrenceCommandHandler
        : IRequestHandler<UpdateRecurringTaskOccurrenceCommand, Result<TaskDetailDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ISubtaskRepository _subtaskRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IRecurringTaskRootRepository _rootRepository;
        private readonly IRecurringTaskSeriesRepository _seriesRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        private readonly IRecurringTaskExceptionRepository _exceptionRepository;
        // REFACTORED: added for recurring-task-attachments feature
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly IRecurringTaskMaterializerService _materializerService;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly RecurringTaskOptions _recurringOptions;

        public UpdateRecurringTaskOccurrenceCommandHandler(ITaskRepository taskRepository,
                                                           ISubtaskRepository subtaskRepository,
                                                           ICategoryRepository categoryRepository,
                                                           IRecurringTaskRootRepository rootRepository,
                                                           IRecurringTaskSeriesRepository seriesRepository,
                                                           IRecurringTaskSubtaskRepository recurringSubtaskRepository,
                                                           IRecurringTaskExceptionRepository exceptionRepository,
                                                           IRecurringTaskAttachmentRepository recurringAttachmentRepository,
                                                           IRecurringTaskMaterializerService materializerService,
                                                           IOutboxRepository outboxRepository,
                                                           IUnitOfWork unitOfWork,
                                                           ICurrentUserService currentUserService,
                                                           ISystemClock clock,
                                                           IOptions<RecurringTaskOptions> recurringOptions)
        {
            _taskRepository = taskRepository;
            _subtaskRepository = subtaskRepository;
            _categoryRepository = categoryRepository;
            _rootRepository = rootRepository;
            _seriesRepository = seriesRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _exceptionRepository = exceptionRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository;
            _materializerService = materializerService;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _recurringOptions = recurringOptions.Value;
        }

        public async Task<Result<TaskDetailDto>> Handle(UpdateRecurringTaskOccurrenceCommand command,
                                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // Validate category ownership before any domain work.
            if (command.CategoryId.HasValue)
            {
                var category = await _categoryRepository.GetByIdUntrackedAsync(command.CategoryId.Value,
                                                                               cancellationToken);

                if (category is null || category.UserId != userId)
                {
                    return Result.Fail<TaskDetailDto>(
                        new Error("Category not found or does not belong to you.")
                            .WithMetadata("ErrorCode", "Categories.NotFound"));
                }
            }

            return command.Scope switch
            {
                RecurringEditScope.Single => await HandleSingleAsync(command, userId, utcNow, cancellationToken),
                RecurringEditScope.ThisAndFollowing => await HandleThisAndFollowingAsync(command, userId, utcNow, cancellationToken),
                RecurringEditScope.All => await HandleAllAsync(command, userId, utcNow, cancellationToken),
                _ => Result.Fail<TaskDetailDto>($"Unknown RecurringEditScope: {command.Scope}")
            };
        }

        // -------------------------------------------------------------------------
        // Single scope
        // -------------------------------------------------------------------------

        private async Task<Result<TaskDetailDto>> HandleSingleAsync(UpdateRecurringTaskOccurrenceCommand command,
                                                                    Guid userId,
                                                                    DateTime utcNow,
                                                                    CancellationToken cancellationToken)
        {
            TaskItem? updatedTask = null;

            if (command.TaskItemId.HasValue)
            {
                // Materialized occurrence — load and update the TaskItem.
                var task = await _taskRepository.GetByIdUntrackedAsync(command.TaskItemId.Value, cancellationToken);

                if (task is null || task.UserId != userId)
                {
                    return Result.Fail<TaskDetailDto>(
                        new Error("Task not found or does not belong to you.")
                            .WithMetadata("ErrorCode", "Tasks.NotFound"));
                }

                // Apply task field updates.
                var updateResult = task.Update(title: command.Title,
                                               date: command.OverrideDate ?? task.Date,
                                               description: command.Description,
                                               startTime: command.StartTime,
                                               endTime: command.EndTime,
                                               location: command.Location,
                                               travelTime: command.TravelTime,
                                               categoryId: command.CategoryId,
                                               priority: command.Priority,
                                               utcNow: utcNow,
                                               meetingLink: command.MeetingLink);

                if (updateResult.IsFailure)
                {
                    return Result.Fail<TaskDetailDto>(
                        updateResult.Errors.Select(e => new Error(e.Message)));
                }

                // Apply reminder if changed.
                if (command.ReminderAtUtc != task.ReminderAtUtc)
                {
                    var reminderResult = task.SetReminder(command.ReminderAtUtc, utcNow);
                    if (reminderResult.IsFailure)
                    {
                        return Result.Fail<TaskDetailDto>(
                            reminderResult.Errors.Select(e => new Error(e.Message)));
                    }
                }

                // Apply completion state if changed.
                if (command.IsCompleted != task.IsCompleted)
                {
                    var completedResult = command.IsCompleted
                        ? task.MarkCompleted(utcNow)
                        : task.MarkPending(utcNow);

                    if (completedResult.IsFailure)
                    {
                        return Result.Fail<TaskDetailDto>(
                            completedResult.Errors.Select(e => new Error(e.Message)));
                    }
                }

                _taskRepository.Update(task);
                updatedTask = task;
            }

            // Upsert the exception so virtual projection and sync pick up the override.
            var existing = await _exceptionRepository.GetByOccurrenceAsync(command.SeriesId,
                                                                           command.OccurrenceDate,
                                                                           cancellationToken);

            if (existing is not null && !existing.IsDeletion)
            {
                // Update the existing override exception.
                var updateOverrideResult = existing.UpdateOverride(overrideTitle: command.Title,
                                                                   overrideDescription: command.Description,
                                                                   overrideDate: command.OverrideDate,
                                                                   overrideStartTime: command.StartTime,
                                                                   overrideEndTime: command.EndTime,
                                                                   overrideLocation: command.Location,
                                                                   overrideTravelTime: command.TravelTime,
                                                                   overrideCategoryId: command.CategoryId,
                                                                   overridePriority: command.Priority,
                                                                   overrideMeetingLink: command.MeetingLink,
                                                                   overrideReminderAtUtc: command.ReminderAtUtc,
                                                                   isCompleted: command.IsCompleted,
                                                                   utcNow: utcNow);

                if (updateOverrideResult.IsFailure)
                {
                    return Result.Fail<TaskDetailDto>(
                        updateOverrideResult.Errors.Select(e => new Error(e.Message)));
                }

                _exceptionRepository.Update(existing);
            }
            else
            {
                // Create a new override exception.
                var exResult = RecurringTaskException.CreateOverride(userId: userId,
                                                                     seriesId: command.SeriesId,
                                                                     occurrenceDate: command.OccurrenceDate,
                                                                     overrideTitle: command.Title,
                                                                     overrideDescription: command.Description,
                                                                     overrideDate: command.OverrideDate,
                                                                     overrideStartTime: command.StartTime,
                                                                     overrideEndTime: command.EndTime,
                                                                     overrideLocation: command.Location,
                                                                     overrideTravelTime: command.TravelTime,
                                                                     overrideCategoryId: command.CategoryId,
                                                                     overridePriority: command.Priority,
                                                                     overrideMeetingLink: command.MeetingLink,
                                                                     overrideReminderAtUtc: command.ReminderAtUtc,
                                                                     isCompleted: command.IsCompleted,
                                                                     materializedTaskItemId: updatedTask?.Id,
                                                                     utcNow: utcNow);

                if (exResult.IsFailure)
                {
                    return Result.Fail<TaskDetailDto>(
                        exResult.Errors.Select(e => new Error(e.Message)));
                }

                await _exceptionRepository.AddAsync(exResult.Value, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Return the updated task DTO (or a minimal fallback for virtual occurrences).
            if (updatedTask is not null)
            {
                return Result.Ok(updatedTask.ToDetailDto());
            }

            // Virtual occurrence: return a synthesized DTO from the override fields.
            return Result.Ok(new TaskDetailDto(
                TaskId: Guid.Empty,
                Title: command.Title,
                Description: command.Description,
                Date: command.OverrideDate ?? command.OccurrenceDate,
                StartTime: command.StartTime,
                EndTime: command.EndTime,
                IsCompleted: command.IsCompleted,
                Location: command.Location,
                TravelTime: command.TravelTime,
                CreatedAtUtc: utcNow,
                UpdatedAtUtc: utcNow,
                ReminderAtUtc: command.ReminderAtUtc,
                CategoryId: command.CategoryId,
                Priority: command.Priority,
                MeetingLink: command.MeetingLink,
                RowVersion: Array.Empty<byte>()));
        }

        // -------------------------------------------------------------------------
        // ThisAndFollowing scope
        // -------------------------------------------------------------------------

        private async Task<Result<TaskDetailDto>> HandleThisAndFollowingAsync(UpdateRecurringTaskOccurrenceCommand command,
                                                                              Guid userId,
                                                                              DateTime utcNow,
                                                                              CancellationToken cancellationToken)
        {
            var today = DateOnly.FromDateTime(utcNow);
            var currentHorizon = today.AddDays(_recurringOptions.HorizonWeeksAhead * 7);

            // Load the current series (untracked — we will call Update after mutation).
            var oldSeries = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (oldSeries is null || oldSeries.UserId != userId)
            {
                return Result.Fail<TaskDetailDto>(
                    new Error("Recurring series not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            // 1. Bulk soft-delete all materialized TaskItems from occurrenceDate forward (change-tracker).
            await _taskRepository.SoftDeleteRecurringFromDateAsync(
                command.SeriesId, command.OccurrenceDate, userId, utcNow, cancellationToken);

            // 2. Bulk soft-delete all exceptions from occurrenceDate forward (change-tracker).
            await _exceptionRepository.SoftDeleteFromDateAsync(
                command.SeriesId, command.OccurrenceDate, userId, utcNow, cancellationToken);

            // 3. Terminate the old series at occurrenceDate.
            var terminateResult = oldSeries.Terminate(command.OccurrenceDate, utcNow);
            if (terminateResult.IsFailure)
            {
                return Result.Fail<TaskDetailDto>(
                    terminateResult.Errors.Select(e => new Error(e.Message)));
            }
            _seriesRepository.Update(oldSeries);

            // 4. Create the new series segment (same RootId, starting from occurrenceDate).
            var newRRuleString = command.NewRRuleString ?? oldSeries.RRuleString;
            var initialMaterializedUpTo = command.OccurrenceDate.AddDays(-1);

            var newSeriesResult = RecurringTaskSeries.Create(userId: userId,
                                                             rootId: oldSeries.RootId,
                                                             rruleString: newRRuleString,
                                                             startsOnDate: command.OccurrenceDate,
                                                             endsBeforeDate: command.NewEndsBeforeDate,
                                                             title: command.Title,
                                                             description: command.Description,
                                                             startTime: command.StartTime,
                                                             endTime: command.EndTime,
                                                             location: command.Location,
                                                             travelTime: command.TravelTime,
                                                             categoryId: command.CategoryId,
                                                             priority: command.Priority,
                                                             meetingLink: command.MeetingLink,
                                                             reminderOffsetMinutes: command.ReminderOffsetMinutes,
                                                             materializedUpToDate: initialMaterializedUpTo,
                                                             utcNow: utcNow);

            if (newSeriesResult.IsFailure)
            {
                return Result.Fail<TaskDetailDto>(
                    newSeriesResult.Errors.Select(e => new Error(e.Message)));
            }

            var newSeries = newSeriesResult.Value;

            // 5. Create template subtasks for the new series.
            var templateSubtasks = new List<RecurringTaskSubtask>();
            if (command.NewTemplateSubtasks is { Count: > 0 })
            {
                foreach (var stDto in command.NewTemplateSubtasks)
                {
                    var stResult = RecurringTaskSubtask.CreateForSeries(userId: userId,
                                                                        seriesId: newSeries.Id,
                                                                        text: stDto.Text,
                                                                        position: stDto.Position,
                                                                        utcNow: utcNow);

                    if (stResult.IsFailure)
                    {
                        return Result.Fail<TaskDetailDto>(
                            stResult.Errors.Select(e => new Error(e.Message)));
                    }

                    templateSubtasks.Add(stResult.Value);
                }
            }

            // 6. Materialize the initial batch for the new series.
            //    Only materialize up to the active horizon (Decision #9 from the plan).
            MaterializationBatch batch;
            if (command.OccurrenceDate <= currentHorizon)
            {
                var batchResult = _materializerService.MaterializeInitialBatch(
                    series: newSeries,
                    templateSubtasks: templateSubtasks,
                    exceptions: [],
                    exceptionSubtasksById: new Dictionary<Guid, IReadOnlyList<RecurringTaskSubtask>>(),
                    utcNow: utcNow,
                    batchSize: _recurringOptions.InitialMaterializationBatchSize);

                if (batchResult.IsFailed)
                    return batchResult.ToResult<TaskDetailDto>();

                batch = batchResult.Value;

                var advanceResult = newSeries.AdvanceMaterializedHorizon(today, utcNow);
                if (advanceResult.IsFailure)
                {
                    return Result.Fail<TaskDetailDto>(
                        advanceResult.Errors.Select(e => new Error(e.Message)));
                }
            }
            else
            {
                // Future series — let the worker handle materialization.
                batch = new MaterializationBatch([], []);
            }

            // 7. Persist everything in one SaveChangesAsync.
            await _seriesRepository.AddAsync(newSeries, cancellationToken);

            foreach (var st in templateSubtasks)
            {
                await _recurringSubtaskRepository.AddAsync(st, cancellationToken);
            }

            // Copy series template attachments from the old series to the new series.
            // Same BlobPath, new row IDs — the orphan-cleanup worker checks ExistsNonDeletedWithBlobPathAsync
            // before deleting any blob, so shared blobs are safe.
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
                    return Result.Fail<TaskDetailDto>(copyResult.Errors.Select(e => new Error(e.Message)));

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

            // Single SaveChangesAsync — soft-deleted tasks, soft-deleted exceptions,
            // terminated series, new series, new template subtasks, new TaskItems all atomic.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Return the first materialized occurrence of the new series.
            if (batch.Tasks.Count > 0)
            {
                return Result.Ok(batch.Tasks[0].ToDetailDto());
            }

            // Future start or empty batch — synthesize DTO from new series.
            return Result.Ok(new TaskDetailDto(
                TaskId: newSeries.Id,
                Title: newSeries.Title,
                Description: newSeries.Description,
                Date: newSeries.StartsOnDate,
                StartTime: newSeries.StartTime,
                EndTime: newSeries.EndTime,
                IsCompleted: false,
                Location: newSeries.Location,
                TravelTime: newSeries.TravelTime,
                CreatedAtUtc: newSeries.CreatedAtUtc,
                UpdatedAtUtc: newSeries.UpdatedAtUtc,
                ReminderAtUtc: null,
                CategoryId: newSeries.CategoryId,
                Priority: newSeries.Priority,
                MeetingLink: newSeries.MeetingLink,
                RowVersion: Array.Empty<byte>()));
        }

        // -------------------------------------------------------------------------
        // All scope
        // -------------------------------------------------------------------------

        private async Task<Result<TaskDetailDto>> HandleAllAsync(UpdateRecurringTaskOccurrenceCommand command,
                                                                 Guid userId,
                                                                 DateTime utcNow,
                                                                 CancellationToken cancellationToken)
        {
            // Resolve series to get the RootId.
            var referenceSeries = await _seriesRepository.GetByIdUntrackedAsync(command.SeriesId, cancellationToken);

            if (referenceSeries is null || referenceSeries.UserId != userId)
            {
                return Result.Fail<TaskDetailDto>(
                    new Error("Recurring series not found or does not belong to you.")
                        .WithMetadata("ErrorCode", "RecurringSeries.NotFound"));
            }

            var rootId = referenceSeries.RootId;

            // 1. Load all active series segments for the root.
            var allSeries = await _seriesRepository.GetActiveByRootIdAsync(rootId, userId, cancellationToken);

            // 2. Update the template fields on every series segment.
            foreach (var series in allSeries)
            {
                var templateResult = series.UpdateTemplate(title: command.Title,
                                                           description: command.Description,
                                                           startTime: command.StartTime,
                                                           endTime: command.EndTime,
                                                           location: command.Location,
                                                           travelTime: command.TravelTime,
                                                           categoryId: command.CategoryId,
                                                           priority: command.Priority,
                                                           meetingLink: command.MeetingLink,
                                                           reminderOffsetMinutes: command.ReminderOffsetMinutes,
                                                           utcNow: utcNow);

                if (templateResult.IsFailure)
                {
                    return Result.Fail<TaskDetailDto>(
                        templateResult.Errors.Select(e => new Error(e.Message)));
                }

                _seriesRepository.Update(series);
            }

            // 3. For each series, load materialized TaskItems and update those that have
            //    no individual exception (individually-modified occurrences are preserved).
            //    We identify "individually modified" by checking which TaskItem IDs appear
            //    as MaterializedTaskItemId on a non-deletion exception.
            foreach (var series in allSeries)
            {
                // Load all exceptions that have a MaterializedTaskItemId (override exceptions only).
                // We use GetForSeriesInRangeAsync with a very wide range as a proxy.
                // Exceptions with MaterializedTaskItemId set = individually modified.
                var tasks = await _taskRepository.GetBySeriesAsync(series.Id, userId, cancellationToken);

                if (tasks.Count == 0) continue;

                // Build a set of TaskItem IDs that are linked to an exception.
                // We do this by checking CanonicalOccurrenceDate against exceptions.
                // A simpler approach: any task whose CanonicalOccurrenceDate has an override
                // exception in the DB is considered individually modified.
                // For "edit all", we skip those and only update the rest.

                // Load exceptions for the series (all, across all time).
                // We use a very wide date range as a proxy for "all exceptions".
                var exceptions = await _exceptionRepository.GetForSeriesInRangeAsync(series.Id,
                                                                                     DateOnly.MinValue,
                                                                                     DateOnly.MaxValue,
                                                                                     cancellationToken);

                // Build a set of canonical occurrence dates that have a non-deletion exception
                // (these are individually-modified occurrences to preserve).
                var individuallyModifiedDates = new HashSet<DateOnly>(
                    exceptions
                        .Where(e => !e.IsDeletion && e.MaterializedTaskItemId.HasValue)
                        .Select(e => e.OccurrenceDate));

                foreach (var task in tasks)
                {
                    // Skip tasks that were individually modified (have their own exception).
                    if (task.CanonicalOccurrenceDate.HasValue &&
                        individuallyModifiedDates.Contains(task.CanonicalOccurrenceDate.Value))
                    {
                        continue;
                    }

                    var taskUpdateResult = task.Update(
                        title: command.Title,
                        date: task.Date, // keep the existing date (do not move occurrences)
                        description: command.Description,
                        startTime: command.StartTime,
                        endTime: command.EndTime,
                        location: command.Location,
                        travelTime: command.TravelTime,
                        categoryId: command.CategoryId,
                        priority: command.Priority,
                        utcNow: utcNow,
                        meetingLink: command.MeetingLink);

                    if (taskUpdateResult.IsFailure)
                    {
                        return Result.Fail<TaskDetailDto>(
                            taskUpdateResult.Errors.Select(e => new Error(e.Message)));
                    }

                    _taskRepository.Update(task);
                }
            }

            // 4. Persist all series template updates + task field updates atomically.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Return a synthesized DTO from the reference series (no specific TaskItem).
            return Result.Ok(new TaskDetailDto(
                TaskId: referenceSeries.Id,
                Title: command.Title,
                Description: command.Description,
                Date: referenceSeries.StartsOnDate,
                StartTime: command.StartTime,
                EndTime: command.EndTime,
                IsCompleted: false,
                Location: command.Location,
                TravelTime: command.TravelTime,
                CreatedAtUtc: referenceSeries.CreatedAtUtc,
                UpdatedAtUtc: utcNow,
                ReminderAtUtc: null,
                CategoryId: command.CategoryId,
                Priority: command.Priority,
                MeetingLink: command.MeetingLink,
                RowVersion: Array.Empty<byte>()));
        }
    }
}
