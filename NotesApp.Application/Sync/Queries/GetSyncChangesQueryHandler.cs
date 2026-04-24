using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.RecurringAttachments;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Handles sync pull requests from client devices.
    ///
    /// Returns all changes (tasks, notes, blocks, assets) since a given timestamp:
    /// - Initial sync (SinceUtc = null): Returns all non-deleted entities
    /// - Incremental sync: Categorises entities into created/updated/deleted buckets
    ///   based on CreatedAtUtc vs SinceUtc comparison
    ///
    /// Features:
    /// - Optional device ownership validation
    /// - Pagination via MaxItemsPerEntity (separate limits per entity type)
    /// - HasMore indicators for truncated results
    ///
    /// Asset download URLs are NOT included in the sync response.
    /// Use GET /api/assets/{id}/download-url to obtain a pre-signed URL on demand.
    ///
    /// Note: This is a read-only query - no outbox messages are emitted.
    /// </summary>
    public sealed class GetSyncChangesQueryHandler
        : IRequestHandler<GetSyncChangesQuery, Result<SyncChangesDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICategoryRepository _categoryRepository; // REFACTORED: added for category sync pull
        private readonly ISubtaskRepository _subtaskRepository; // REFACTORED: added for subtask sync pull
        private readonly IAttachmentRepository _attachmentRepository; // REFACTORED: added for task-attachments sync pull
        // REFACTORED: added recurring-task repos for recurring-tasks feature
        private readonly IRecurringTaskRootRepository _recurringRootRepository;
        private readonly IRecurringTaskSeriesRepository _recurringSeriesRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSubtaskRepository;
        private readonly IRecurringTaskExceptionRepository _recurringExceptionRepository;
        // REFACTORED: added for recurring-task-attachments feature
        private readonly IRecurringTaskAttachmentRepository _recurringAttachmentRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetSyncChangesQueryHandler> _logger;

        public GetSyncChangesQueryHandler(ITaskRepository taskRepository,
                                          INoteRepository noteRepository,
                                          IBlockRepository blockRepository,
                                          IAssetRepository assetRepository,
                                          IUserDeviceRepository deviceRepository,
                                          ICategoryRepository categoryRepository,
                                          ISubtaskRepository subtaskRepository,
                                          IAttachmentRepository attachmentRepository,
                                          IRecurringTaskRootRepository recurringRootRepository,
                                          IRecurringTaskSeriesRepository recurringSeriesRepository,
                                          IRecurringTaskSubtaskRepository recurringSubtaskRepository,
                                          IRecurringTaskExceptionRepository recurringExceptionRepository,
                                          IRecurringTaskAttachmentRepository recurringAttachmentRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetSyncChangesQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;
            _assetRepository = assetRepository;
            _deviceRepository = deviceRepository;
            _categoryRepository = categoryRepository; // REFACTORED: added for category sync pull
            _subtaskRepository = subtaskRepository; // REFACTORED: added for subtask sync pull
            _attachmentRepository = attachmentRepository; // REFACTORED: added for task-attachments sync pull
            // REFACTORED: added recurring-task repos for recurring-tasks feature
            _recurringRootRepository = recurringRootRepository;
            _recurringSeriesRepository = recurringSeriesRepository;
            _recurringSubtaskRepository = recurringSubtaskRepository;
            _recurringExceptionRepository = recurringExceptionRepository;
            _recurringAttachmentRepository = recurringAttachmentRepository; // REFACTORED: added for recurring-task-attachments feature
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<SyncChangesDto>> Handle(GetSyncChangesQuery request,
                                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Optional device ownership / status check
            if (request.DeviceId is Guid deviceId)
            {
                var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

                if (device is null ||
                    device.UserId != userId ||
                    !device.IsActive ||
                    device.IsDeleted)
                {
                    return Result.Fail(new Error("Device.NotFound"));
                }
            }

            _logger.LogInformation("Sync pull requested for user {UserId} since {SinceUtc} with device {DeviceId}",
                                   userId,
                                   request.SinceUtc,
                                   request.DeviceId);

            // Fetch all changed entities
            var tasks = await _taskRepository.GetChangedSinceAsync(userId,
                                                                   request.SinceUtc,
                                                                   cancellationToken);

            var notes = await _noteRepository.GetChangedSinceAsync(userId,
                                                                   request.SinceUtc,
                                                                   cancellationToken);

            var blocks = await _blockRepository.GetChangedSinceAsync(userId,
                                                                     request.SinceUtc,
                                                                     cancellationToken);

            var assets = await _assetRepository.GetChangedSinceAsync(userId,
                                                                     request.SinceUtc,
                                                                     cancellationToken);

            // REFACTORED: fetch category changes for sync pull
            var categories = await _categoryRepository.GetChangedSinceAsync(userId,
                                                                             request.SinceUtc,
                                                                             cancellationToken);

            // REFACTORED: fetch subtask changes for sync pull
            var subtasks = await _subtaskRepository.GetChangedSinceAsync(userId,
                                                                          request.SinceUtc,
                                                                          cancellationToken);

            // REFACTORED: fetch attachment changes for sync pull (task-attachments feature)
            var attachments = await _attachmentRepository.GetChangedSinceAsync(userId,
                                                                                request.SinceUtc,
                                                                                cancellationToken);

            // REFACTORED: fetch recurring-task entity changes for recurring-tasks feature
            var recurringRoots = await _recurringRootRepository.GetChangedSinceAsync(userId,
                                                                                      request.SinceUtc,
                                                                                      cancellationToken);

            var recurringSeries = await _recurringSeriesRepository.GetChangedSinceAsync(userId,
                                                                                         request.SinceUtc,
                                                                                         cancellationToken);

            var recurringSubtasks = await _recurringSubtaskRepository.GetChangedSinceAsync(userId,
                                                                                             request.SinceUtc,
                                                                                             cancellationToken);

            var recurringExceptions = await _recurringExceptionRepository.GetChangedSinceAsync(userId,
                                                                                                request.SinceUtc,
                                                                                                cancellationToken);

            // REFACTORED: fetch recurring attachment changes for sync pull (recurring-task-attachments feature)
            var recurringAttachments = await _recurringAttachmentRepository.GetChangedSinceAsync(
                userId, request.SinceUtc, cancellationToken);

            // Batch-load exception subtasks for inlining in RecurringExceptionSyncItemDto.Subtasks.
            // Only non-deleted exceptions can have subtasks worth inlining.
            var nonDeletedExceptionIds = recurringExceptions
                .Where(e => !e.IsDeleted && !e.IsDeletion)
                .Select(e => e.Id)
                .ToList();

            var allExceptionSubtasks = nonDeletedExceptionIds.Count > 0
                ? await _recurringSubtaskRepository.GetByExceptionIdsAsync(nonDeletedExceptionIds, cancellationToken)
                : (IReadOnlyList<RecurringTaskSubtask>)Array.Empty<RecurringTaskSubtask>();

            // Group exception subtasks by ExceptionId for O(1) lookup during mapping.
            var exceptionSubtasksByExceptionId = allExceptionSubtasks
                .GroupBy(s => s.ExceptionId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RecurringSubtaskSyncItemDto>)g.Select(s => s.ToSyncDto()).ToList());

            // Batch-load exception attachments for inlining in RecurringExceptionSyncItemDto.Attachments.
            // Only non-deleted, HasAttachmentOverride exceptions have exception-scoped attachments.
            var overrideExceptionIds = recurringExceptions
                .Where(e => !e.IsDeleted && !e.IsDeletion && e.HasAttachmentOverride)
                .Select(e => e.Id)
                .ToList();

            var allExceptionAttachments = overrideExceptionIds.Count > 0
                ? await _recurringAttachmentRepository.GetByExceptionIdsAsync(overrideExceptionIds, cancellationToken)
                : (IReadOnlyList<RecurringTaskAttachment>)Array.Empty<RecurringTaskAttachment>();

            var exceptionAttachmentsByExceptionId = allExceptionAttachments
                .GroupBy(a => a.ExceptionId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<RecurringAttachmentSyncItemDto>)g.Select(a => a.ToSyncDto()).ToList());

            var serverTimestampUtc = DateTime.UtcNow;

            // Categorise entities into buckets
            var tasksBuckets = CategoriseTasks(tasks, request.SinceUtc);
            var notesBuckets = CategoriseNotes(notes, request.SinceUtc);
            var blocksBuckets = CategoriseBlocks(blocks, request.SinceUtc);
            var assetsBuckets = CategoriseAssets(assets, request.SinceUtc);
            var categoriesBuckets = CategoriseCategories(categories, request.SinceUtc); // REFACTORED: added categories bucket
            var subtasksBuckets = CategoriseSubtasks(subtasks, request.SinceUtc); // REFACTORED: added subtasks bucket
            var attachmentsBuckets = CategoriseAttachments(attachments, request.SinceUtc); // REFACTORED: added attachments bucket
            // REFACTORED: categorise recurring-task entities for recurring-tasks feature
            var recurringRootsBuckets = CategoriseRecurringRoots(recurringRoots, request.SinceUtc);
            var recurringSeriesBuckets = CategoriseRecurringSeries(recurringSeries, request.SinceUtc);
            var recurringSubtasksBuckets = CategoriseRecurringSubtasks(recurringSubtasks, request.SinceUtc);
            var recurringExceptionsBuckets = CategoriseRecurringExceptions(recurringExceptions, request.SinceUtc, exceptionSubtasksByExceptionId, exceptionAttachmentsByExceptionId);
            // REFACTORED: categorise recurring attachment changes (recurring-task-attachments feature)
            var recurringAttachmentsBuckets = CategoriseRecurringAttachments(recurringAttachments, request.SinceUtc);

            // Determine effective max items per entity
            var effectiveMax = request.MaxItemsPerEntity ?? SyncLimits.DefaultPullMaxItemsPerEntity;

            // Materialise lists so we can safely truncate them
            var taskCreated = tasksBuckets.Created.ToList();
            var taskUpdated = tasksBuckets.Updated.ToList();
            var taskDeleted = tasksBuckets.Deleted.ToList();

            var noteCreated = notesBuckets.Created.ToList();
            var noteUpdated = notesBuckets.Updated.ToList();
            var noteDeleted = notesBuckets.Deleted.ToList();

            var blockCreated = blocksBuckets.Created.ToList();
            var blockUpdated = blocksBuckets.Updated.ToList();
            var blockDeleted = blocksBuckets.Deleted.ToList();

            var assetCreated = assetsBuckets.Created.ToList();
            var assetDeleted = assetsBuckets.Deleted.ToList();

            // REFACTORED: materialise category lists for pagination
            var categoryCreated = categoriesBuckets.Created.ToList();
            var categoryUpdated = categoriesBuckets.Updated.ToList();
            var categoryDeleted = categoriesBuckets.Deleted.ToList();

            // REFACTORED: materialise subtask lists for pagination
            var subtaskCreated = subtasksBuckets.Created.ToList();
            var subtaskUpdated = subtasksBuckets.Updated.ToList();
            var subtaskDeleted = subtasksBuckets.Deleted.ToList();

            // REFACTORED: materialise recurring-task lists for pagination (recurring-tasks feature)
            var recurringRootCreated = recurringRootsBuckets.Created.ToList();
            var recurringRootUpdated = recurringRootsBuckets.Updated.ToList();
            var recurringRootDeleted = recurringRootsBuckets.Deleted.ToList();

            var recurringSeriesCreated = recurringSeriesBuckets.Created.ToList();
            var recurringSeriesUpdated = recurringSeriesBuckets.Updated.ToList();
            var recurringSeriesDeleted = recurringSeriesBuckets.Deleted.ToList();

            var recurringSubtaskCreated = recurringSubtasksBuckets.Created.ToList();
            var recurringSubtaskUpdated = recurringSubtasksBuckets.Updated.ToList();
            var recurringSubtaskDeleted = recurringSubtasksBuckets.Deleted.ToList();

            var recurringExceptionCreated = recurringExceptionsBuckets.Created.ToList();
            var recurringExceptionUpdated = recurringExceptionsBuckets.Updated.ToList();
            var recurringExceptionDeleted = recurringExceptionsBuckets.Deleted.ToList();

            // REFACTORED: materialise recurring attachment lists (recurring-task-attachments feature)
            var recurringAttachmentCreated = recurringAttachmentsBuckets.Created.ToList();
            var recurringAttachmentDeleted = recurringAttachmentsBuckets.Deleted.ToList();

            // Calculate totals for pagination indicators
            var totalTaskChanges = taskCreated.Count + taskUpdated.Count + taskDeleted.Count;
            var totalNoteChanges = noteCreated.Count + noteUpdated.Count + noteDeleted.Count;
            var totalBlockChanges = blockCreated.Count + blockUpdated.Count + blockDeleted.Count;
            var totalCategoryChanges = categoryCreated.Count + categoryUpdated.Count + categoryDeleted.Count; // REFACTORED
            var totalSubtaskChanges = subtaskCreated.Count + subtaskUpdated.Count + subtaskDeleted.Count; // REFACTORED

            // REFACTORED: totals for recurring-task entities (recurring-tasks feature)
            var totalRecurringRootChanges = recurringRootCreated.Count + recurringRootUpdated.Count + recurringRootDeleted.Count;
            var totalRecurringSeriesChanges = recurringSeriesCreated.Count + recurringSeriesUpdated.Count + recurringSeriesDeleted.Count;
            var totalRecurringSubtaskChanges = recurringSubtaskCreated.Count + recurringSubtaskUpdated.Count + recurringSubtaskDeleted.Count;
            var totalRecurringExceptionChanges = recurringExceptionCreated.Count + recurringExceptionUpdated.Count + recurringExceptionDeleted.Count;

            var hasMoreTasks = totalTaskChanges > effectiveMax;
            var hasMoreNotes = totalNoteChanges > effectiveMax;
            var hasMoreBlocks = totalBlockChanges > effectiveMax;
            var hasMoreCategories = totalCategoryChanges > effectiveMax; // REFACTORED: aligned with convention
            var hasMoreSubtasks = totalSubtaskChanges > effectiveMax; // REFACTORED: subtasks pagination flag
            // REFACTORED: recurring-task pagination flags (recurring-tasks feature)
            var hasMoreRecurringRoots = totalRecurringRootChanges > effectiveMax;
            var hasMoreRecurringSeries = totalRecurringSeriesChanges > effectiveMax;
            var hasMoreRecurringSeriesSubtasks = totalRecurringSubtaskChanges > effectiveMax;
            var hasMoreRecurringExceptions = totalRecurringExceptionChanges > effectiveMax;
            // REFACTORED: recurring attachment pagination (recurring-task-attachments feature)
            // Volume is bounded by MaxAttachmentsPerTask × series count; follow the no-limit pattern of Attachments.
            var hasMoreRecurringAttachments = false;

            static List<T> LimitList<T>(List<T> source, ref int remaining)
            {
                if (remaining <= 0)
                {
                    return new List<T>();
                }

                if (source.Count <= remaining)
                {
                    remaining -= source.Count;
                    return source;
                }

                var result = source.Take(remaining).ToList();
                remaining = 0;
                return result;
            }

            // Apply limit per entity type independently
            var taskRemaining = effectiveMax;
            var limitedTaskCreated = LimitList(taskCreated, ref taskRemaining);
            var limitedTaskUpdated = LimitList(taskUpdated, ref taskRemaining);
            var limitedTaskDeleted = LimitList(taskDeleted, ref taskRemaining);

            var noteRemaining = effectiveMax;
            var limitedNoteCreated = LimitList(noteCreated, ref noteRemaining);
            var limitedNoteUpdated = LimitList(noteUpdated, ref noteRemaining);
            var limitedNoteDeleted = LimitList(noteDeleted, ref noteRemaining);

            var blockRemaining = effectiveMax;
            var limitedBlockCreated = LimitList(blockCreated, ref blockRemaining);
            var limitedBlockUpdated = LimitList(blockUpdated, ref blockRemaining);
            var limitedBlockDeleted = LimitList(blockDeleted, ref blockRemaining);

            // Assets don't have a limit currently (they're typically fewer and essential)
            var limitedAssetCreated = assetCreated;
            var limitedAssetDeleted = assetDeleted;

            // REFACTORED: attachments follow the same no-limit pattern as assets;
            // volume is bounded by MaxAttachmentsPerTask per task
            var attachmentCreated = attachmentsBuckets.Created.ToList();
            var attachmentDeleted = attachmentsBuckets.Deleted.ToList();
            var limitedAttachmentCreated = attachmentCreated;
            var limitedAttachmentDeleted = attachmentDeleted;

            // REFACTORED: categories use the same effectiveMax as all other entity types
            var categoryRemaining = effectiveMax;
            var limitedCategoryCreated = LimitList(categoryCreated, ref categoryRemaining);
            var limitedCategoryUpdated = LimitList(categoryUpdated, ref categoryRemaining);
            var limitedCategoryDeleted = LimitList(categoryDeleted, ref categoryRemaining);

            // REFACTORED: subtasks use the same effectiveMax as all other entity types
            var subtaskRemaining = effectiveMax;
            var limitedSubtaskCreated = LimitList(subtaskCreated, ref subtaskRemaining);
            var limitedSubtaskUpdated = LimitList(subtaskUpdated, ref subtaskRemaining);
            var limitedSubtaskDeleted = LimitList(subtaskDeleted, ref subtaskRemaining);

            // REFACTORED: recurring-task entities use the same effectiveMax (recurring-tasks feature)
            var recurringRootRemaining = effectiveMax;
            var limitedRecurringRootCreated = LimitList(recurringRootCreated, ref recurringRootRemaining);
            var limitedRecurringRootUpdated = LimitList(recurringRootUpdated, ref recurringRootRemaining);
            var limitedRecurringRootDeleted = LimitList(recurringRootDeleted, ref recurringRootRemaining);

            var recurringSeriesRemaining = effectiveMax;
            var limitedRecurringSeriesCreated = LimitList(recurringSeriesCreated, ref recurringSeriesRemaining);
            var limitedRecurringSeriesUpdated = LimitList(recurringSeriesUpdated, ref recurringSeriesRemaining);
            var limitedRecurringSeriesDeleted = LimitList(recurringSeriesDeleted, ref recurringSeriesRemaining);

            var recurringSubtaskRemaining = effectiveMax;
            var limitedRecurringSubtaskCreated = LimitList(recurringSubtaskCreated, ref recurringSubtaskRemaining);
            var limitedRecurringSubtaskUpdated = LimitList(recurringSubtaskUpdated, ref recurringSubtaskRemaining);
            var limitedRecurringSubtaskDeleted = LimitList(recurringSubtaskDeleted, ref recurringSubtaskRemaining);

            var recurringExceptionRemaining = effectiveMax;
            var limitedRecurringExceptionCreated = LimitList(recurringExceptionCreated, ref recurringExceptionRemaining);
            var limitedRecurringExceptionUpdated = LimitList(recurringExceptionUpdated, ref recurringExceptionRemaining);
            var limitedRecurringExceptionDeleted = LimitList(recurringExceptionDeleted, ref recurringExceptionRemaining);

            var limitedTasksBuckets = new SyncTasksChangesDto
            {
                Created = limitedTaskCreated,
                Updated = limitedTaskUpdated,
                Deleted = limitedTaskDeleted
            };

            var limitedNotesBuckets = new SyncNotesChangesDto
            {
                Created = limitedNoteCreated,
                Updated = limitedNoteUpdated,
                Deleted = limitedNoteDeleted
            };

            var limitedBlocksBuckets = new SyncBlocksChangesDto
            {
                Created = limitedBlockCreated,
                Updated = limitedBlockUpdated,
                Deleted = limitedBlockDeleted
            };

            var limitedAssetsBuckets = new SyncAssetsChangesDto
            {
                Created = limitedAssetCreated,
                Deleted = limitedAssetDeleted
            };

            // REFACTORED: build limited attachments bucket (task-attachments feature)
            var limitedAttachmentsBuckets = new SyncAttachmentsChangesDto
            {
                Created = limitedAttachmentCreated,
                Deleted = limitedAttachmentDeleted
            };

            // REFACTORED: build limited categories bucket
            var limitedCategoriesBuckets = new SyncCategoriesChangesDto
            {
                Created = limitedCategoryCreated,
                Updated = limitedCategoryUpdated,
                Deleted = limitedCategoryDeleted
            };

            // REFACTORED: build limited subtasks bucket
            var limitedSubtasksBuckets = new SyncSubtasksChangesDto
            {
                Created = limitedSubtaskCreated,
                Updated = limitedSubtaskUpdated,
                Deleted = limitedSubtaskDeleted
            };

            // REFACTORED: build limited recurring-task buckets (recurring-tasks feature)
            var limitedRecurringRootsBuckets = new SyncRecurringRootsChangesDto
            {
                Created = limitedRecurringRootCreated,
                Updated = limitedRecurringRootUpdated,
                Deleted = limitedRecurringRootDeleted
            };

            var limitedRecurringSeriesBuckets = new SyncRecurringSeriesChangesDto
            {
                Created = limitedRecurringSeriesCreated,
                Updated = limitedRecurringSeriesUpdated,
                Deleted = limitedRecurringSeriesDeleted
            };

            var limitedRecurringSeriesSubtasksBuckets = new SyncRecurringSeriesSubtasksChangesDto
            {
                Created = limitedRecurringSubtaskCreated,
                Updated = limitedRecurringSubtaskUpdated,
                Deleted = limitedRecurringSubtaskDeleted
            };

            var limitedRecurringExceptionsBuckets = new SyncRecurringExceptionsChangesDto
            {
                Created = limitedRecurringExceptionCreated,
                Updated = limitedRecurringExceptionUpdated,
                Deleted = limitedRecurringExceptionDeleted
            };

            // REFACTORED: build limited recurring attachments bucket (recurring-task-attachments feature)
            var limitedRecurringAttachmentsBuckets = new SyncRecurringAttachmentsChangesDto
            {
                Created = recurringAttachmentCreated,
                Deleted = recurringAttachmentDeleted
            };

            var dto = new SyncChangesDto
            {
                ServerTimestampUtc = serverTimestampUtc,
                Tasks = limitedTasksBuckets,
                Notes = limitedNotesBuckets,
                Blocks = limitedBlocksBuckets,
                Assets = limitedAssetsBuckets,
                Categories = limitedCategoriesBuckets, // REFACTORED: added categories
                Subtasks = limitedSubtasksBuckets, // REFACTORED: added subtasks
                Attachments = limitedAttachmentsBuckets, // REFACTORED: added attachments
                // REFACTORED: added recurring-task buckets for recurring-tasks feature
                RecurringRoots = limitedRecurringRootsBuckets,
                RecurringSeries = limitedRecurringSeriesBuckets,
                RecurringSeriesSubtasks = limitedRecurringSeriesSubtasksBuckets,
                RecurringExceptions = limitedRecurringExceptionsBuckets,
                // REFACTORED: added recurring attachments bucket (recurring-task-attachments feature)
                RecurringAttachments = limitedRecurringAttachmentsBuckets,
                HasMoreTasks = hasMoreTasks,
                HasMoreNotes = hasMoreNotes,
                HasMoreBlocks = hasMoreBlocks,
                HasMoreCategories = hasMoreCategories, // REFACTORED: added categories pagination flag
                HasMoreSubtasks = hasMoreSubtasks, // REFACTORED: added subtasks pagination flag
                // REFACTORED: recurring-task pagination flags (recurring-tasks feature)
                HasMoreRecurringRoots = hasMoreRecurringRoots,
                HasMoreRecurringSeries = hasMoreRecurringSeries,
                HasMoreRecurringSeriesSubtasks = hasMoreRecurringSeriesSubtasks,
                HasMoreRecurringExceptions = hasMoreRecurringExceptions,
                HasMoreRecurringAttachments = hasMoreRecurringAttachments
            };

            return Result.Ok(dto);
        }

        private static SyncTasksChangesDto CategoriseTasks(IReadOnlyList<TaskItem> tasks,
                                                           DateTime? sinceUtc)
        {
            // Initial sync (since == null): everything is "created" (non-deleted only,
            // because the repository already filters out deleted items in that branch).
            if (sinceUtc is null)
            {
                var created = tasks
                    .Where(t => !t.IsDeleted)
                    .OrderBy(t => t.UpdatedAtUtc)
                    .Select(t => t.ToSyncDto())
                    .ToList();

                return new SyncTasksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<TaskSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<TaskSyncItemDto>();
            var updatedList = new List<TaskSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var task in tasks.OrderBy(t => t.UpdatedAtUtc))
            {
                if (task.IsDeleted)
                {
                    // Treat any deleted item in the window as "deleted" regardless of CreatedAtUtc,
                    // using UpdatedAtUtc as the deletion timestamp.
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = task.Id,
                        DeletedAtUtc = task.UpdatedAtUtc
                    });

                    continue;
                }

                if (task.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(task.ToSyncDto());
                }
                else
                {
                    updatedList.Add(task.ToSyncDto());
                }
            }

            return new SyncTasksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncNotesChangesDto CategoriseNotes(IReadOnlyList<Note> notes,
                                                           DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = notes
                    .Where(n => !n.IsDeleted)
                    .OrderBy(n => n.UpdatedAtUtc)
                    .Select(n => n.ToSyncDto())
                    .ToList();

                return new SyncNotesChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<NoteSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<NoteSyncItemDto>();
            var updatedList = new List<NoteSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var note in notes.OrderBy(n => n.UpdatedAtUtc))
            {
                if (note.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = note.Id,
                        DeletedAtUtc = note.UpdatedAtUtc
                    });

                    continue;
                }

                if (note.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(note.ToSyncDto());
                }
                else
                {
                    updatedList.Add(note.ToSyncDto());
                }
            }

            return new SyncNotesChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncBlocksChangesDto CategoriseBlocks(IReadOnlyList<Block> blocks,
                                                             DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = blocks
                    .Where(b => !b.IsDeleted)
                    .OrderBy(b => b.UpdatedAtUtc)
                    .Select(b => b.ToSyncDto())
                    .ToList();

                return new SyncBlocksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<BlockSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<BlockSyncItemDto>();
            var updatedList = new List<BlockSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var block in blocks.OrderBy(b => b.UpdatedAtUtc))
            {
                if (block.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = block.Id,
                        DeletedAtUtc = block.UpdatedAtUtc
                    });

                    continue;
                }

                if (block.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(block.ToSyncDto());
                }
                else
                {
                    updatedList.Add(block.ToSyncDto());
                }
            }

            return new SyncBlocksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        /// <summary>
        /// Categorises assets into created/deleted buckets.
        /// Assets are immutable, so there's no "updated" bucket.
        /// Download URLs are not included; clients should call GET /api/assets/{id}/download-url on demand.
        /// </summary>
        private static SyncAssetsChangesDto CategoriseAssets(IReadOnlyList<Asset> assets, DateTime? sinceUtc)
        {
            var createdList = new List<AssetSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var asset in assets.OrderBy(a => a.UpdatedAtUtc))
            {
                if (asset.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = asset.Id,
                        DeletedAtUtc = asset.UpdatedAtUtc
                    });

                    continue;
                }

                createdList.Add(asset.ToSyncDto());
            }

            return new SyncAssetsChangesDto
            {
                Created = createdList,
                Deleted = deletedList
            };
        }

        // REFACTORED: added CategoriseCategories method for task categories feature
        /// <summary>
        /// Categorises category changes into created/updated/deleted buckets.
        /// Mirrors <see cref="CategoriseTasks"/> — uses <c>CreatedAtUtc &gt; sinceUtc</c>
        /// to distinguish created from updated, and <c>IsDeleted</c> for the deleted bucket.
        /// Categories are typically small per-user lists, so a separate
        /// <see cref="SyncLimits.DefaultPullMaxCategories"/> ceiling is applied by the caller.
        /// </summary>
        private static SyncCategoriesChangesDto CategoriseCategories(
            IReadOnlyList<TaskCategory> categories, DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = categories
                    .Where(c => !c.IsDeleted)
                    .OrderBy(c => c.UpdatedAtUtc)
                    .Select(c => c.ToSyncDto())
                    .ToList();

                return new SyncCategoriesChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<CategorySyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<CategorySyncItemDto>();
            var updatedList = new List<CategorySyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var category in categories.OrderBy(c => c.UpdatedAtUtc))
            {
                if (category.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = category.Id,
                        DeletedAtUtc = category.UpdatedAtUtc
                    });

                    continue;
                }

                if (category.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(category.ToSyncDto());
                }
                else
                {
                    updatedList.Add(category.ToSyncDto());
                }
            }

            return new SyncCategoriesChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        // REFACTORED: added CategoriseSubtasks method for subtasks feature
        /// <summary>
        /// Categorises subtask changes into created/updated/deleted buckets.
        /// Mirrors <see cref="CategoriseCategories"/> — uses <c>CreatedAtUtc &gt; sinceUtc</c>
        /// to distinguish created from updated, and <c>IsDeleted</c> for the deleted bucket.
        /// </summary>
        private static SyncSubtasksChangesDto CategoriseSubtasks(
            IReadOnlyList<Subtask> subtasks, DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = subtasks
                    .Where(s => !s.IsDeleted)
                    .OrderBy(s => s.UpdatedAtUtc)
                    .Select(s => s.ToSyncDto())
                    .ToList();

                return new SyncSubtasksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<SubtaskSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<SubtaskSyncItemDto>();
            var updatedList = new List<SubtaskSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var subtask in subtasks.OrderBy(s => s.UpdatedAtUtc))
            {
                if (subtask.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = subtask.Id,
                        DeletedAtUtc = subtask.UpdatedAtUtc
                    });

                    continue;
                }

                if (subtask.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(subtask.ToSyncDto());
                }
                else
                {
                    updatedList.Add(subtask.ToSyncDto());
                }
            }

            return new SyncSubtasksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        // REFACTORED: added CategoriseAttachments method for task-attachments feature
        /// <summary>
        /// Categorises attachment changes into created/deleted buckets.
        /// Attachments are immutable, so there is no "updated" bucket.
        /// Mirrors <see cref="CategoriseAssets"/> exactly.
        /// </summary>
        private static SyncAttachmentsChangesDto CategoriseAttachments(
            IReadOnlyList<Attachment> attachments, DateTime? sinceUtc)
        {
            var createdList = new List<AttachmentSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var attachment in attachments.OrderBy(a => a.UpdatedAtUtc))
            {
                if (attachment.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = attachment.Id,
                        DeletedAtUtc = attachment.UpdatedAtUtc
                    });

                    continue;
                }

                createdList.Add(attachment.ToSyncDto());
            }

            return new SyncAttachmentsChangesDto
            {
                Created = createdList,
                Deleted = deletedList
            };
        }

        // REFACTORED: recurring-task categorise methods for recurring-tasks feature

        private static SyncRecurringRootsChangesDto CategoriseRecurringRoots(
            IReadOnlyList<RecurringTaskRoot> roots, DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = roots
                    .Where(r => !r.IsDeleted)
                    .OrderBy(r => r.UpdatedAtUtc)
                    .Select(r => r.ToSyncDto())
                    .ToList();

                return new SyncRecurringRootsChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<RecurringRootSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<RecurringRootSyncItemDto>();
            var updatedList = new List<RecurringRootSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var root in roots.OrderBy(r => r.UpdatedAtUtc))
            {
                if (root.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto { Id = root.Id, DeletedAtUtc = root.UpdatedAtUtc });
                    continue;
                }

                if (root.CreatedAtUtc > sinceUtc.Value)
                    createdList.Add(root.ToSyncDto());
                else
                    updatedList.Add(root.ToSyncDto());
            }

            return new SyncRecurringRootsChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncRecurringSeriesChangesDto CategoriseRecurringSeries(
            IReadOnlyList<RecurringTaskSeries> seriesList, DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = seriesList
                    .Where(s => !s.IsDeleted)
                    .OrderBy(s => s.UpdatedAtUtc)
                    .Select(s => s.ToSyncDto())
                    .ToList();

                return new SyncRecurringSeriesChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<RecurringSeriesSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<RecurringSeriesSyncItemDto>();
            var updatedList = new List<RecurringSeriesSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var series in seriesList.OrderBy(s => s.UpdatedAtUtc))
            {
                if (series.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto { Id = series.Id, DeletedAtUtc = series.UpdatedAtUtc });
                    continue;
                }

                if (series.CreatedAtUtc > sinceUtc.Value)
                    createdList.Add(series.ToSyncDto());
                else
                    updatedList.Add(series.ToSyncDto());
            }

            return new SyncRecurringSeriesChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncRecurringSeriesSubtasksChangesDto CategoriseRecurringSubtasks(
            IReadOnlyList<RecurringTaskSubtask> subtasks, DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = subtasks
                    .Where(s => !s.IsDeleted)
                    .OrderBy(s => s.UpdatedAtUtc)
                    .Select(s => s.ToSyncDto())
                    .ToList();

                return new SyncRecurringSeriesSubtasksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<RecurringSubtaskSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<RecurringSubtaskSyncItemDto>();
            var updatedList = new List<RecurringSubtaskSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var subtask in subtasks.OrderBy(s => s.UpdatedAtUtc))
            {
                if (subtask.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto { Id = subtask.Id, DeletedAtUtc = subtask.UpdatedAtUtc });
                    continue;
                }

                if (subtask.CreatedAtUtc > sinceUtc.Value)
                    createdList.Add(subtask.ToSyncDto());
                else
                    updatedList.Add(subtask.ToSyncDto());
            }

            return new SyncRecurringSeriesSubtasksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        // REFACTORED: updated signature to include attachments for recurring-task-attachments feature
        private static SyncRecurringExceptionsChangesDto CategoriseRecurringExceptions(
            IReadOnlyList<RecurringTaskException> exceptions,
            DateTime? sinceUtc,
            Dictionary<Guid, IReadOnlyList<RecurringSubtaskSyncItemDto>> exceptionSubtasksById,
            Dictionary<Guid, IReadOnlyList<RecurringAttachmentSyncItemDto>> exceptionAttachmentsById)
        {
            static IReadOnlyList<RecurringSubtaskSyncItemDto> GetSubtasks(
                RecurringTaskException ex,
                Dictionary<Guid, IReadOnlyList<RecurringSubtaskSyncItemDto>> lookup)
            {
                if (ex.IsDeleted || ex.IsDeletion)
                    return Array.Empty<RecurringSubtaskSyncItemDto>();

                return lookup.TryGetValue(ex.Id, out var subs) ? subs : Array.Empty<RecurringSubtaskSyncItemDto>();
            }

            static IReadOnlyList<RecurringAttachmentSyncItemDto> GetAttachments(
                RecurringTaskException ex,
                Dictionary<Guid, IReadOnlyList<RecurringAttachmentSyncItemDto>> lookup)
            {
                if (ex.IsDeleted || ex.IsDeletion || !ex.HasAttachmentOverride)
                    return Array.Empty<RecurringAttachmentSyncItemDto>();

                return lookup.TryGetValue(ex.Id, out var atts) ? atts : Array.Empty<RecurringAttachmentSyncItemDto>();
            }

            if (sinceUtc is null)
            {
                var created = exceptions
                    .Where(e => !e.IsDeleted)
                    .OrderBy(e => e.UpdatedAtUtc)
                    .Select(e => e.ToSyncDto(GetSubtasks(e, exceptionSubtasksById), GetAttachments(e, exceptionAttachmentsById)))
                    .ToList();

                return new SyncRecurringExceptionsChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<RecurringExceptionSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<RecurringExceptionSyncItemDto>();
            var updatedList = new List<RecurringExceptionSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var exception in exceptions.OrderBy(e => e.UpdatedAtUtc))
            {
                if (exception.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto { Id = exception.Id, DeletedAtUtc = exception.UpdatedAtUtc });
                    continue;
                }

                var subtasks = GetSubtasks(exception, exceptionSubtasksById);
                var attachments = GetAttachments(exception, exceptionAttachmentsById);

                if (exception.CreatedAtUtc > sinceUtc.Value)
                    createdList.Add(exception.ToSyncDto(subtasks, attachments));
                else
                    updatedList.Add(exception.ToSyncDto(subtasks, attachments));
            }

            return new SyncRecurringExceptionsChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        // REFACTORED: added CategoriseRecurringAttachments for recurring-task-attachments feature
        /// <summary>
        /// Categorises recurring attachment changes into created/deleted buckets.
        /// Recurring task attachments are immutable, so there is no "updated" bucket.
        /// Mirrors <see cref="CategoriseAttachments"/> exactly.
        /// </summary>
        private static SyncRecurringAttachmentsChangesDto CategoriseRecurringAttachments(
            IReadOnlyList<RecurringTaskAttachment> attachments, DateTime? sinceUtc)
        {
            var createdList = new List<RecurringAttachmentSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var attachment in attachments.OrderBy(a => a.UpdatedAtUtc))
            {
                if (attachment.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = attachment.Id,
                        DeletedAtUtc = attachment.UpdatedAtUtc
                    });

                    continue;
                }

                createdList.Add(attachment.ToSyncDto());
            }

            return new SyncRecurringAttachmentsChangesDto
            {
                Created = createdList,
                Deleted = deletedList
            };
        }
    }
}
