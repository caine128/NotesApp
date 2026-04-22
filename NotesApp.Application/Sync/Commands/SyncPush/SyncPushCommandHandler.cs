using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Sync.Commands.SyncPush
{
    /// <summary>
    /// Handles sync push from client devices:
    /// - Applies creates/updates/deletes for tasks, notes, blocks, and categories.
    /// - Categories are processed BEFORE tasks so within-push CategoryId references resolve.
    /// - Uses Version for optimistic concurrency on updates.
    /// - Always uses "delete wins" semantics for deletes.
    /// - Emits outbox messages for Created / Updated / Deleted events.
    /// - Category deletes do NOT cascade to tasks server-side; the mobile client sends all
    ///   affected task updates (CategoryId = null) in the same push payload.
    ///
    /// Per-item conflicts (version mismatch, not found, etc.) are embedded
    /// directly in each result DTO's Conflict property rather than in a separate list.
    /// </summary>
    public sealed class SyncPushCommandHandler
        : IRequestHandler<SyncPushCommand, Result<SyncPushResultDto>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICategoryRepository _categoryRepository; // REFACTORED: added for category push processing
        private readonly ISubtaskRepository _subtaskRepository; // REFACTORED: added for subtask push processing
        private readonly IAttachmentRepository _attachmentRepository; // REFACTORED: added for task-attachments push processing
        // REFACTORED: added recurring-task repositories for recurring-tasks feature
        private readonly IRecurringTaskRootRepository _recurringRootRepository;
        private readonly IRecurringTaskSeriesRepository _recurringSeriesRepository;
        private readonly IRecurringTaskSubtaskRepository _recurringSeriesSubtaskRepository;
        private readonly IRecurringTaskExceptionRepository _recurringExceptionRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<SyncPushCommandHandler> _logger;

        public SyncPushCommandHandler(ICurrentUserService currentUserService,
                                     ITaskRepository taskRepository,
                                     INoteRepository noteRepository,
                                     IBlockRepository blockRepository,
                                     IUserDeviceRepository deviceRepository,
                                     ICategoryRepository categoryRepository,
                                     ISubtaskRepository subtaskRepository,
                                     IAttachmentRepository attachmentRepository,
                                     IRecurringTaskRootRepository recurringRootRepository,
                                     IRecurringTaskSeriesRepository recurringSeriesRepository,
                                     IRecurringTaskSubtaskRepository recurringSeriesSubtaskRepository,
                                     IRecurringTaskExceptionRepository recurringExceptionRepository,
                                     IOutboxRepository outboxRepository,
                                     IUnitOfWork unitOfWork,
                                     ISystemClock clock,
                                     ILogger<SyncPushCommandHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;
            _deviceRepository = deviceRepository;
            _categoryRepository = categoryRepository; // REFACTORED: added for category push processing
            _subtaskRepository = subtaskRepository; // REFACTORED: added for subtask push processing
            _attachmentRepository = attachmentRepository; // REFACTORED: added for task-attachments push processing
            // REFACTORED: added recurring-task repositories for recurring-tasks feature
            _recurringRootRepository = recurringRootRepository;
            _recurringSeriesRepository = recurringSeriesRepository;
            _recurringSeriesSubtaskRepository = recurringSeriesSubtaskRepository;
            _recurringExceptionRepository = recurringExceptionRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<SyncPushResultDto>> Handle(SyncPushCommand request,
                                                    CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // Device ownership / status check
            var device = await _deviceRepository.GetByIdAsync(request.DeviceId, cancellationToken);
            if (device is null ||
                device.UserId != userId ||
                !device.IsActive ||
                device.IsDeleted)
            {
                return Result.Fail(new Error("Device.NotFound"));
            }

            _logger.LogInformation("Sync push received from device {DeviceId} for user {UserId} at {UtcNow}",
                                   request.DeviceId,
                                   userId,
                                   utcNow);

            var taskCreateResults = new List<TaskCreatedPushResultDto>();
            var taskUpdateResults = new List<TaskUpdatedPushResultDto>();
            var taskDeleteResults = new List<TaskDeletedPushResultDto>();

            var noteCreateResults = new List<NoteCreatedPushResultDto>();
            var noteUpdateResults = new List<NoteUpdatedPushResultDto>();
            var noteDeleteResults = new List<NoteDeletedPushResultDto>();

            var blockCreateResults = new List<BlockCreatedPushResultDto>();
            var blockUpdateResults = new List<BlockUpdatedPushResultDto>();
            var blockDeleteResults = new List<BlockDeletedPushResultDto>();

            // REFACTORED: added category result lists for task categories feature
            var categoryCreateResults = new List<CategoryCreatedPushResultDto>();
            var categoryUpdateResults = new List<CategoryUpdatedPushResultDto>();
            var categoryDeleteResults = new List<CategoryDeletedPushResultDto>();

            // REFACTORED: added subtask result lists for subtasks feature
            var subtaskCreateResults = new List<SubtaskCreatedPushResultDto>();
            var subtaskUpdateResults = new List<SubtaskUpdatedPushResultDto>();
            var subtaskDeleteResults = new List<SubtaskDeletedPushResultDto>();

            // REFACTORED: added attachment result list for task-attachments feature
            var attachmentDeleteResults = new List<AttachmentDeletedPushResultDto>();

            // REFACTORED: added recurring-task result lists for recurring-tasks feature
            var recurringRootCreateResults = new List<RecurringRootCreatedPushResultDto>();
            var recurringRootDeleteResults = new List<RecurringRootDeletedPushResultDto>();
            var recurringSeriesCreateResults = new List<RecurringSeriesCreatedPushResultDto>();
            var recurringSeriesUpdateResults = new List<RecurringSeriesUpdatedPushResultDto>();
            var recurringSeriesDeleteResults = new List<RecurringSeriesDeletedPushResultDto>();
            var recurringSubtaskCreateResults = new List<RecurringSubtaskCreatedPushResultDto>();
            var recurringSubtaskUpdateResults = new List<RecurringSubtaskUpdatedPushResultDto>();
            var recurringSubtaskDeleteResults = new List<RecurringSubtaskDeletedPushResultDto>();
            var recurringExceptionCreateResults = new List<RecurringExceptionCreatedPushResultDto>();
            var recurringExceptionUpdateResults = new List<RecurringExceptionUpdatedPushResultDto>();
            var recurringExceptionDeleteResults = new List<RecurringExceptionDeletedPushResultDto>();

            // Maps client IDs to server IDs for entities created in this push.
            // Used to resolve parent references for blocks.
            var taskClientToServerIds = new Dictionary<Guid, Guid>();
            var noteClientToServerIds = new Dictionary<Guid, Guid>();

            // REFACTORED: categories must be processed BEFORE tasks so that within-push CategoryId
            // references (task.CategoryId pointing to a category ClientId from the same push) resolve
            // correctly via categoryClientToServerIds.
            var categoryClientToServerIds = new Dictionary<Guid, Guid>();

            // REFACTORED: recurring entity client-to-server ID mappings for recurring-tasks feature.
            // rootClientToServerIds: allows RecurringSeries.RootClientId to reference a root created
            //   in the same push.
            // seriesClientToServerIds: allows RecurringSeriesSubtasks.SeriesClientId and
            //   Task.RecurringSeriesClientId to reference a series created in the same push.
            var rootClientToServerIds = new Dictionary<Guid, Guid>();
            var seriesClientToServerIds = new Dictionary<Guid, Guid>();

            await ProcessCategoryCreatesAsync(userId,
                                              request,
                                              utcNow,
                                              categoryCreateResults,
                                              categoryClientToServerIds,
                                              cancellationToken);

            await ProcessCategoryUpdatesAsync(userId,
                                              request,
                                              utcNow,
                                              categoryUpdateResults,
                                              cancellationToken);

            await ProcessCategoryDeletesAsync(userId,
                                              request,
                                              utcNow,
                                              categoryDeleteResults,
                                              cancellationToken);

            // REFACTORED: process recurring entities BEFORE tasks so within-push series references
            // (Task.RecurringSeriesClientId) can be resolved via seriesClientToServerIds.
            // Processing order per plan: Categories → RecurringRoots → RecurringSeries →
            // RecurringSeriesSubtasks → RecurringExceptions → Tasks (recurring-tasks feature).
            await ProcessRecurringRootCreatesAsync(userId,
                                                   request,
                                                   utcNow,
                                                   recurringRootCreateResults,
                                                   rootClientToServerIds,
                                                   cancellationToken);

            await ProcessRecurringRootDeletesAsync(userId,
                                                   request,
                                                   utcNow,
                                                   recurringRootDeleteResults,
                                                   cancellationToken);

            await ProcessRecurringSeriesCreatesAsync(userId,
                                                     request,
                                                     utcNow,
                                                     recurringSeriesCreateResults,
                                                     rootClientToServerIds,
                                                     seriesClientToServerIds,
                                                     cancellationToken);

            await ProcessRecurringSeriesUpdatesAsync(userId,
                                                     request,
                                                     utcNow,
                                                     recurringSeriesUpdateResults,
                                                     cancellationToken);

            await ProcessRecurringSeriesDeletesAsync(userId,
                                                     request,
                                                     utcNow,
                                                     recurringSeriesDeleteResults,
                                                     cancellationToken);

            await ProcessRecurringSubtaskCreatesAsync(userId,
                                                      request,
                                                      utcNow,
                                                      recurringSubtaskCreateResults,
                                                      seriesClientToServerIds,
                                                      cancellationToken);

            await ProcessRecurringSubtaskUpdatesAsync(userId,
                                                      request,
                                                      utcNow,
                                                      recurringSubtaskUpdateResults,
                                                      cancellationToken);

            await ProcessRecurringSubtaskDeletesAsync(userId,
                                                      request,
                                                      utcNow,
                                                      recurringSubtaskDeleteResults,
                                                      cancellationToken);

            await ProcessRecurringExceptionCreatesAsync(userId,
                                                        request,
                                                        utcNow,
                                                        recurringExceptionCreateResults,
                                                        cancellationToken);

            await ProcessRecurringExceptionUpdatesAsync(userId,
                                                        request,
                                                        utcNow,
                                                        recurringExceptionUpdateResults,
                                                        cancellationToken);

            await ProcessRecurringExceptionDeletesAsync(userId,
                                                        request,
                                                        utcNow,
                                                        recurringExceptionDeleteResults,
                                                        cancellationToken);

            // Process tasks (collect client->server ID mappings)
            // Pass categoryClientToServerIds so CategoryId can be resolved for within-push categories.
            // Pass seriesClientToServerIds so RecurringSeriesClientId can be resolved for within-push series.
            await ProcessTaskCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskCreateResults,
                                          taskClientToServerIds,
                                          categoryClientToServerIds, // REFACTORED
                                          seriesClientToServerIds,   // REFACTORED: recurring-tasks feature
                                          cancellationToken);

            await ProcessTaskUpdatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskUpdateResults,
                                          categoryClientToServerIds, // REFACTORED
                                          cancellationToken);

            await ProcessTaskDeletesAsync(userId,
                                          request,
                                          utcNow,
                                          taskDeleteResults,
                                          cancellationToken);

            // Process notes (collect client->server ID mappings)
            await ProcessNoteCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          noteCreateResults,
                                          noteClientToServerIds,
                                          cancellationToken);

            await ProcessNoteUpdatesAsync(userId,
                                          request,
                                          utcNow,
                                          noteUpdateResults,
                                          cancellationToken);

            await ProcessNoteDeletesAsync(userId,
                                          request,
                                          utcNow,
                                          noteDeleteResults,
                                          cancellationToken);

            // REFACTORED: process subtasks after tasks so taskClientToServerIds is fully populated (subtasks feature)
            await ProcessSubtaskCreatesAsync(userId,
                                             request,
                                             utcNow,
                                             subtaskCreateResults,
                                             taskClientToServerIds,
                                             cancellationToken);

            await ProcessSubtaskUpdatesAsync(userId,
                                             request,
                                             utcNow,
                                             subtaskUpdateResults,
                                             cancellationToken);

            await ProcessSubtaskDeletesAsync(userId,
                                             request,
                                             utcNow,
                                             subtaskDeleteResults,
                                             cancellationToken);

            // REFACTORED: process attachment deletes after task deletes so same-push task-delete +
            // attachment-delete pairs cascade cleanly (explicit attachment deletes become AlreadyDeleted)
            // task-attachments feature
            await ProcessAttachmentDeletesAsync(userId,
                                                request,
                                                utcNow,
                                                attachmentDeleteResults,
                                                cancellationToken);

            // Process blocks (after tasks/notes so parent ID mappings are available)
            await ProcessBlockCreatesAsync(userId,
                                           request,
                                           utcNow,
                                           blockCreateResults,
                                           taskClientToServerIds,
                                           noteClientToServerIds,
                                           cancellationToken);

            await ProcessBlockUpdatesAsync(userId,
                                           request,
                                           utcNow,
                                           blockUpdateResults,
                                           cancellationToken);

            await ProcessBlockDeletesAsync(userId,
                                           request,
                                           utcNow,
                                           blockDeleteResults,
                                           cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var resultDto = new SyncPushResultDto
            {
                Tasks = new SyncPushTasksResultDto
                {
                    Created = taskCreateResults,
                    Updated = taskUpdateResults,
                    Deleted = taskDeleteResults
                },
                Notes = new SyncPushNotesResultDto
                {
                    Created = noteCreateResults,
                    Updated = noteUpdateResults,
                    Deleted = noteDeleteResults
                },
                Blocks = new SyncPushBlocksResultDto
                {
                    Created = blockCreateResults,
                    Updated = blockUpdateResults,
                    Deleted = blockDeleteResults
                },
                // REFACTORED: added category results for task categories feature
                Categories = new SyncPushCategoriesResultDto
                {
                    Created = categoryCreateResults,
                    Updated = categoryUpdateResults,
                    Deleted = categoryDeleteResults
                },
                // REFACTORED: added subtask results for subtasks feature
                Subtasks = new SyncPushSubtasksResultDto
                {
                    Created = subtaskCreateResults,
                    Updated = subtaskUpdateResults,
                    Deleted = subtaskDeleteResults
                },
                // REFACTORED: added attachment results for task-attachments feature
                Attachments = new SyncPushAttachmentsResultDto
                {
                    Deleted = attachmentDeleteResults
                },
                // REFACTORED: added recurring-task results for recurring-tasks feature
                RecurringRoots = new SyncPushRecurringRootsResultDto
                {
                    Created = recurringRootCreateResults,
                    Deleted = recurringRootDeleteResults
                },
                RecurringSeries = new SyncPushRecurringSeriesResultDto
                {
                    Created = recurringSeriesCreateResults,
                    Updated = recurringSeriesUpdateResults,
                    Deleted = recurringSeriesDeleteResults
                },
                RecurringSeriesSubtasks = new SyncPushRecurringSeriesSubtasksResultDto
                {
                    Created = recurringSubtaskCreateResults,
                    Updated = recurringSubtaskUpdateResults,
                    Deleted = recurringSubtaskDeleteResults
                },
                RecurringExceptions = new SyncPushRecurringExceptionsResultDto
                {
                    Created = recurringExceptionCreateResults,
                    Updated = recurringExceptionUpdateResults,
                    Deleted = recurringExceptionDeleteResults
                },
            };

            return Result.Ok(resultDto);
        }




        // ------------------------
        // Task processing
        // ------------------------

        private async Task ProcessTaskCreatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<TaskCreatedPushResultDto> results,
                                                   Dictionary<Guid, Guid> clientToServerIds,
                                                   Dictionary<Guid, Guid> categoryClientToServerIds, // REFACTORED: within-push category resolution
                                                   Dictionary<Guid, Guid> seriesClientToServerIds,   // REFACTORED: within-push series resolution (recurring-tasks feature)
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Created)
            {
                // REFACTORED: resolve CategoryId — if it's a within-push client Id, map it to the
                // server Id that ProcessCategoryCreatesAsync stored in categoryClientToServerIds.
                // Otherwise treat it as a server-side Id and validate ownership below.
                Guid? resolvedCategoryId = null;
                if (item.CategoryId.HasValue)
                {
                    if (categoryClientToServerIds.TryGetValue(item.CategoryId.Value, out var mappedServerId))
                    {
                        resolvedCategoryId = mappedServerId;
                    }
                    else
                    {
                        // It's a server-side Id — load and check ownership
                        var category = await _categoryRepository.GetByIdUntrackedAsync(item.CategoryId.Value, cancellationToken);
                        if (category is null || category.UserId != userId)
                        {
                            results.Add(new TaskCreatedPushResultDto
                            {
                                ClientId = item.ClientId,
                                ServerId = Guid.Empty,
                                Version = 0,
                                Status = SyncPushCreatedStatus.Failed,
                                Conflict = new SyncPushConflictDetailDto
                                {
                                    ConflictType = SyncConflictType.ValidationFailed,
                                    Errors = new[] { "Category not found or does not belong to you." }
                                }
                            });

                            continue;
                        }

                        resolvedCategoryId = item.CategoryId.Value;
                    }
                }

                var createResult = TaskItem.Create(userId,
                                                   item.Date,
                                                   item.Title,
                                                   item.Description,
                                                   item.StartTime,
                                                   item.EndTime,
                                                   item.Location,
                                                   item.TravelTime,
                                                   resolvedCategoryId, // REFACTORED: pass resolved CategoryId
                                                   item.Priority, // REFACTORED: pass Priority
                                                   utcNow,
                                                   item.MeetingLink); // REFACTORED: pass MeetingLink

                if (createResult.IsFailure)
                {

                    results.Add(new TaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var task = createResult.Value;

                // Apply reminder if specified and check result
                if (item.ReminderAtUtc is not null)
                {
                    var reminderResult = task.SetReminder(item.ReminderAtUtc.Value, utcNow);
                    if (reminderResult.IsFailure)
                    {
                        results.Add(new TaskCreatedPushResultDto
                        {
                            ClientId = item.ClientId,
                            ServerId = Guid.Empty,
                            Version = 0,
                            Status = SyncPushCreatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = reminderResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }
                }

                // REFACTORED: link to recurring series if provided (recurring-tasks feature).
                // Called here — after task creation and before outbox — so the series link is
                // persisted atomically with the task row. LinkToSeries() throws if called twice.
                if (item.CanonicalOccurrenceDate.HasValue)
                {
                    Guid? resolvedSeriesId = null;

                    if (item.RecurringSeriesClientId.HasValue && item.RecurringSeriesClientId.Value != Guid.Empty)
                    {
                        // Series was created in the same push — resolve via mapping.
                        if (seriesClientToServerIds.TryGetValue(item.RecurringSeriesClientId.Value, out var mapped)
                            && mapped != Guid.Empty)
                        {
                            resolvedSeriesId = mapped;
                        }
                    }
                    else if (item.RecurringSeriesId.HasValue && item.RecurringSeriesId.Value != Guid.Empty)
                    {
                        resolvedSeriesId = item.RecurringSeriesId.Value;
                    }

                    if (resolvedSeriesId.HasValue)
                    {
                        task.LinkToSeries(resolvedSeriesId.Value, item.CanonicalOccurrenceDate.Value);
                    }
                }

                // Create outbox message BEFORE adding task to repository
                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);

                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new TaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Both entity and outbox message created successfully - add both to repositories
                await _taskRepository.AddAsync(task, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                // Store client-to-server ID mapping for block parent resolution
                clientToServerIds[item.ClientId] = task.Id;

                results.Add(new TaskCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = task.Id,
                    Version = task.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        private async Task ProcessTaskUpdatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<TaskUpdatedPushResultDto> results,
                                                   Dictionary<Guid, Guid> categoryClientToServerIds, // REFACTORED: within-push category resolution
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Updated)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var task = await _taskRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (task is null)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (task.IsDeleted)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (task.Version != item.ExpectedVersion)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = task.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = task.Version,
                            ServerTask = task.ToSyncDto()
                        }
                    });

                    continue;
                }

                // REFACTORED: resolve CategoryId for within-push category references
                Guid? resolvedCategoryId = null;
                if (item.CategoryId.HasValue)
                {
                    if (categoryClientToServerIds.TryGetValue(item.CategoryId.Value, out var mappedServerId))
                    {
                        resolvedCategoryId = mappedServerId;
                    }
                    else
                    {
                        var category = await _categoryRepository.GetByIdUntrackedAsync(item.CategoryId.Value, cancellationToken);
                        if (category is null || category.UserId != userId)
                        {
                            results.Add(new TaskUpdatedPushResultDto
                            {
                                Id = item.Id,
                                NewVersion = null,
                                Status = SyncPushUpdatedStatus.Failed,
                                Conflict = new SyncPushConflictDetailDto
                                {
                                    ConflictType = SyncConflictType.ValidationFailed,
                                    Errors = new[] { "Category not found or does not belong to you." }
                                }
                            });

                            continue;
                        }

                        resolvedCategoryId = item.CategoryId.Value;
                    }
                }

                // Modify entity in memory (NOT tracked, won't auto-persist)
                var updateResult = task.Update(item.Title,
                                               item.Date,
                                               item.Description,
                                               item.StartTime,
                                               item.EndTime,
                                               item.Location,
                                               item.TravelTime,
                                               resolvedCategoryId, // REFACTORED: pass resolved CategoryId
                                               item.Priority, // REFACTORED: pass Priority
                                               utcNow,
                                               item.MeetingLink); // REFACTORED: pass MeetingLink

                if (updateResult.IsFailure)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = task.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = task.Version,
                            Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var reminderResult = task.SetReminder(item.ReminderAtUtc, utcNow);
                if (reminderResult.IsFailure)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = reminderResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Create outbox message
                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);
                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    // Entity was modified but NOT tracked - won't be saved
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // SUCCESS: Attach modified entity and add outbox
                _taskRepository.Update(task);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new TaskUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = task.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        private async Task ProcessTaskDeletesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<TaskDeletedPushResultDto> results,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Deleted)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var task = await _taskRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (task is null)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (task.IsDeleted)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                // Create outbox message FIRST (entity not yet modified)
                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);
                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Outbox created successfully - now safe to mark as deleted
                var deleteResult = task.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // SUCCESS: Attach modified entity and add outbox
                _taskRepository.Update(task);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                // The client is responsible for sending explicit SubtaskDeleted items
                // in the same push payload alongside the TaskDeleted item.
                // ProcessSubtaskDeletesAsync (which runs after this method) will handle them.
                // No server-side sweep is performed here.

                results.Add(new TaskDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }




        // ------------------------
        // Note processing
        // ------------------------

        private async Task ProcessNoteCreatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<NoteCreatedPushResultDto> results,
                                                   Dictionary<Guid, Guid> clientToServerIds,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Created)
            {
                var createResult = Note.Create(userId,
                                               item.Date,
                                               item.Title,
                                               item.Summary,
                                               item.Tags,
                                               utcNow);

                if (createResult.IsFailure)
                {
                    results.Add(new NoteCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var note = createResult.Value;

               // Create outbox message BEFORE adding note to repository
                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(note,
                                                                             NoteEventType.Created,
                                                                             payload,
                                                                             utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new NoteCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Both entity and outbox message created successfully - add both to repositories
                await _noteRepository.AddAsync(note, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                // Store client-to-server ID mapping for block parent resolution
                clientToServerIds[item.ClientId] = note.Id;

                results.Add(new NoteCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = note.Id,
                    Version = note.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        private async Task ProcessNoteUpdatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<NoteUpdatedPushResultDto> results,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Updated)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var note = await _noteRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (note is null)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (note.IsDeleted)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (note.Version != item.ExpectedVersion)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = note.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = note.Version,
                            ServerNote = note.ToSyncDto()
                        }
                    });

                    continue;
                }

                // Modify entity in memory (NOT tracked, won't auto-persist)
                var updateResult = note.Update(item.Title,
                                               item.Summary,
                                               item.Tags,
                                               item.Date,
                                               utcNow);

                if (updateResult.IsFailure)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = note.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = note.Version,
                            Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Create outbox message
                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);
                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                    note,
                    NoteEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    // Entity was modified but NOT tracked - won't be saved
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // SUCCESS: Attach modified entity and add outbox
                _noteRepository.Update(note);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new NoteUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = note.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        private async Task ProcessNoteDeletesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<NoteDeletedPushResultDto> results,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Deleted)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var note = await _noteRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (note is null)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (note.IsDeleted)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                // Create outbox message FIRST (entity not yet modified)
                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);
                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                    note,
                    NoteEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Outbox created successfully - now safe to mark as deleted
                var deleteResult = note.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // SUCCESS: Attach modified entity and add outbox
                _noteRepository.Update(note);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new NoteDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }


        // ------------------------
        // Block processing
        // ------------------------

        private async Task ProcessBlockCreatesAsync(Guid userId,
                                                    SyncPushCommand request,
                                                    DateTime utcNow,
                                                    List<BlockCreatedPushResultDto> results,
                                                    Dictionary<Guid, Guid> taskClientToServerIds,
                                                    Dictionary<Guid, Guid> noteClientToServerIds,
                                                    CancellationToken cancellationToken)
        {
            foreach (var item in request.Blocks.Created)
            {
                // Resolve parent ID: use server ID if provided, otherwise resolve from client ID
                Guid? resolvedParentId = ResolveParentId(item.ParentId,
                                                         item.ParentClientId,
                                                         item.ParentType,
                                                         taskClientToServerIds,
                                                         noteClientToServerIds);

                if (!resolvedParentId.HasValue)
                {
                    results.Add(new BlockCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ParentNotFound,
                            Errors = new[] { $"Could not resolve parent ID for block. ParentId: {item.ParentId}, ParentClientId: {item.ParentClientId}" }
                        }
                    });

                    continue;
                }

                DomainResult<Block> createResult;

                // Determine if this is a text block or an asset block
                if (Block.IsTextBlockType(item.Type))
                {
                    createResult = Block.CreateTextBlock(userId,
                                                         resolvedParentId.Value,
                                                         item.ParentType,
                                                         item.Type,
                                                         item.Position,
                                                         item.TextContent,
                                                         utcNow);
                }
                else if (Block.IsAssetBlockType(item.Type))
                {
                    // Validate asset metadata is provided
                    if (string.IsNullOrWhiteSpace(item.AssetClientId) ||
                        string.IsNullOrWhiteSpace(item.AssetFileName) ||
                        !item.AssetSizeBytes.HasValue ||
                        item.AssetSizeBytes.Value <= 0)
                    {
                        results.Add(new BlockCreatedPushResultDto
                        {
                            ClientId = item.ClientId,
                            ServerId = Guid.Empty,
                            Version = 0,
                            Status = SyncPushCreatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = new[] { "Asset blocks require AssetClientId, AssetFileName, and positive AssetSizeBytes." }
                            }
                        });

                        continue;
                    }

                    createResult = Block.CreateAssetBlock(userId,
                                                          resolvedParentId.Value,
                                                          item.ParentType,
                                                          item.Type,
                                                          item.Position,
                                                          item.AssetClientId,
                                                          item.AssetFileName,
                                                          item.AssetContentType,
                                                          item.AssetSizeBytes.Value,
                                                          utcNow);
                }
                else
                {
                    results.Add(new BlockCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = new[] { $"Unknown block type: {item.Type}" }
                        }
                    });

                    continue;
                }

                if (createResult.IsFailure)
                {
                    results.Add(new BlockCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var block = createResult.Value!;

                // Create outbox message BEFORE adding block to repository
                var payload = OutboxPayloadBuilder.BuildBlockPayload(block, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                    block,
                    BlockEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new BlockCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Both entity and outbox message created successfully - add both to repositories
                await _blockRepository.AddAsync(block, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new BlockCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = block.Id,
                    Version = block.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        private async Task ProcessBlockUpdatesAsync(Guid userId,
                                                    SyncPushCommand request,
                                                    DateTime utcNow,
                                                    List<BlockUpdatedPushResultDto> results,
                                                    CancellationToken cancellationToken)
        {
            foreach (var item in request.Blocks.Updated)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var block = await _blockRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (block is null)
                {
                    results.Add(new BlockUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                // Verify ownership
                if (block.UserId != userId)
                {
                    results.Add(new BlockUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (block.IsDeleted)
                {
                    results.Add(new BlockUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (block.Version != item.ExpectedVersion)
                {
                    results.Add(new BlockUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = block.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = block.Version,
                            ServerBlock = block.ToSyncDto()
                        }
                    });

                    continue;
                }

                var hasChanges = false;

                // Update position if provided
                if (!string.IsNullOrEmpty(item.Position))
                {
                    var positionResult = block.UpdatePosition(item.Position, utcNow);
                    if (positionResult.IsFailure)
                    {
                        results.Add(new BlockUpdatedPushResultDto
                        {
                            Id = item.Id,
                            NewVersion = block.Version,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                ClientVersion = item.ExpectedVersion,
                                ServerVersion = block.Version,
                                Errors = positionResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }
                    hasChanges = true;
                }

                // Update text content if provided (only for text blocks)
                if (item.TextContent is not null && Block.IsTextBlockType(block.Type))
                {
                    var contentResult = block.UpdateTextContent(item.TextContent, utcNow);
                    if (contentResult.IsFailure)
                    {
                        results.Add(new BlockUpdatedPushResultDto
                        {
                            Id = item.Id,
                            NewVersion = block.Version,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                ClientVersion = item.ExpectedVersion,
                                ServerVersion = block.Version,
                                Errors = contentResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    var payload = OutboxPayloadBuilder.BuildBlockPayload(block, request.DeviceId);

                    var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                        block,
                        BlockEventType.Updated,
                        payload,
                        utcNow);

                    if (outboxResult.IsFailure)
                    {
                        results.Add(new BlockUpdatedPushResultDto
                        {
                            Id = item.Id,
                            NewVersion = null,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.OutboxFailed,
                                Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    // SUCCESS: Attach modified entity and add outbox
                    _blockRepository.Update(block);
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }

                results.Add(new BlockUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = block.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        private async Task ProcessBlockDeletesAsync(Guid userId,
                                                    SyncPushCommand request,
                                                    DateTime utcNow,
                                                    List<BlockDeletedPushResultDto> results,
                                                    CancellationToken cancellationToken)
        {
            foreach (var item in request.Blocks.Deleted)
            {
                // Load WITHOUT tracking - modifications won't auto-persist
                var block = await _blockRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (block is null)
                {
                    results.Add(new BlockDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                // Verify ownership
                if (block.UserId != userId)
                {
                    results.Add(new BlockDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (block.IsDeleted)
                {
                    results.Add(new BlockDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                // Create outbox message FIRST (entity not yet modified)
                var payload = OutboxPayloadBuilder.BuildBlockPayload(block, request.DeviceId);
                var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                    block,
                    BlockEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new BlockDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Outbox created successfully - now safe to mark as deleted
                var deleteResult = block.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new BlockDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // SUCCESS: Attach modified entity and add outbox
                _blockRepository.Update(block);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new BlockDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        // ------------------------
        // REFACTORED: Category processing (task categories feature)
        // ------------------------

        /// <summary>
        /// Processes category creates from the push payload.
        /// Populates <paramref name="categoryClientToServerIds"/> so subsequent task processing
        /// can resolve CategoryId references to within-push categories.
        /// </summary>
        private async Task ProcessCategoryCreatesAsync(Guid userId,
                                                       SyncPushCommand request,
                                                       DateTime utcNow,
                                                       List<CategoryCreatedPushResultDto> results,
                                                       Dictionary<Guid, Guid> categoryClientToServerIds,
                                                       CancellationToken cancellationToken)
        {
            foreach (var item in request.Categories.Created)
            {
                var createResult = TaskCategory.Create(userId, item.Name, utcNow);

                if (createResult.IsFailure)
                {
                    results.Add(new CategoryCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var category = createResult.Value;

                var payload = JsonSerializer.Serialize(new
                {
                    CategoryId = category.Id,
                    category.UserId,
                    category.Name,
                    category.Version,
                    Event = TaskCategoryEventType.Created.ToString(),
                    OccurredAtUtc = utcNow
                });

                var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                    category,
                    TaskCategoryEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new CategoryCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                await _categoryRepository.AddAsync(category, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                // Store mapping so task creates/updates in this push can resolve CategoryId
                categoryClientToServerIds[item.ClientId] = category.Id;

                results.Add(new CategoryCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = category.Id,
                    Version = category.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        /// <summary>
        /// Processes category updates (renames) from the push payload.
        /// Returns a VersionMismatch conflict when the client's ExpectedVersion doesn't match.
        /// </summary>
        private async Task ProcessCategoryUpdatesAsync(Guid userId,
                                                       SyncPushCommand request,
                                                       DateTime utcNow,
                                                       List<CategoryUpdatedPushResultDto> results,
                                                       CancellationToken cancellationToken)
        {
            foreach (var item in request.Categories.Updated)
            {
                var category = await _categoryRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (category is null || category.UserId != userId)
                {
                    results.Add(new CategoryUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (category.IsDeleted)
                {
                    results.Add(new CategoryUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (category.Version != item.ExpectedVersion)
                {
                    results.Add(new CategoryUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = category.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = category.Version,
                            ServerCategory = category.ToSyncDto()
                        }
                    });

                    continue;
                }

                var updateResult = category.Update(item.Name, utcNow);

                if (updateResult.IsFailure)
                {
                    results.Add(new CategoryUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = category.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = category.Version,
                            Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    CategoryId = category.Id,
                    category.UserId,
                    category.Name,
                    category.Version,
                    Event = TaskCategoryEventType.Updated.ToString(),
                    OccurredAtUtc = utcNow
                });

                var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                    category,
                    TaskCategoryEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new CategoryUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _categoryRepository.Update(category);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new CategoryUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = category.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        /// <summary>
        /// Processes category deletes from the push payload.
        /// Uses "delete wins" semantics — no cascade to tasks server-side.
        /// The mobile client is responsible for sending all affected task updates
        /// (CategoryId = null, incremented version) in the same push payload.
        /// </summary>
        private async Task ProcessCategoryDeletesAsync(Guid userId,
                                                       SyncPushCommand request,
                                                       DateTime utcNow,
                                                       List<CategoryDeletedPushResultDto> results,
                                                       CancellationToken cancellationToken)
        {
            foreach (var item in request.Categories.Deleted)
            {
                var category = await _categoryRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (category is null || category.UserId != userId)
                {
                    results.Add(new CategoryDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (category.IsDeleted)
                {
                    results.Add(new CategoryDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    CategoryId = category.Id,
                    category.UserId,
                    category.Name,
                    category.Version,
                    Event = TaskCategoryEventType.Deleted.ToString(),
                    OccurredAtUtc = utcNow
                });

                var outboxResult = OutboxMessage.Create<TaskCategory, TaskCategoryEventType>(
                    category,
                    TaskCategoryEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new CategoryDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var deleteResult = category.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new CategoryDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _categoryRepository.Update(category);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new CategoryDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        /// <summary>
        /// Resolves the server ID for a block's parent entity.
        /// Returns the ParentId if provided, otherwise looks up the ParentClientId
        /// in the appropriate mapping dictionary.
        /// </summary>
        private static Guid? ResolveParentId(Guid? parentId,
                                             Guid? parentClientId,
                                             BlockParentType parentType,
                                             Dictionary<Guid, Guid> taskClientToServerIds,
                                             Dictionary<Guid, Guid> noteClientToServerIds)
        {
            // If server ID is provided, use it directly
            if (parentId.HasValue && parentId.Value != Guid.Empty)
            {
                return parentId.Value;
            }

            // Otherwise, try to resolve from client ID
            if (!parentClientId.HasValue || parentClientId.Value == Guid.Empty)
            {
                return null;
            }

            // CHANGED: Only Note is supported as parent (Tasks don't have blocks)
            if (parentType != BlockParentType.Note)
            {
                return null;
            }

            return noteClientToServerIds.TryGetValue(parentClientId.Value, out var serverId)
                ? serverId
                : null;
        }


        // ------------------------
        // REFACTORED: Subtask processing (subtasks feature)
        // Subtasks are processed after tasks so that within-push task references
        // (SubtaskCreatedPushItemDto.TaskClientId) can be resolved via taskClientToServerIds.
        // ------------------------

        private async Task ProcessSubtaskCreatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<SubtaskCreatedPushResultDto> results,
            Dictionary<Guid, Guid> taskClientToServerIds,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.Subtasks.Created)
            {
                // Resolve the parent TaskId.
                // Priority: try within-push mapping (TaskClientId) first; fall back to direct server ID.
                Guid? resolvedTaskId = null;

                if (item.TaskClientId.HasValue && item.TaskClientId.Value != Guid.Empty)
                {
                    // Parent was created in the same push — look it up in the mappings dictionary.
                    if (taskClientToServerIds.TryGetValue(item.TaskClientId.Value, out var mappedTaskId))
                    {
                        resolvedTaskId = mappedTaskId;
                    }
                }
                else if (item.TaskId.HasValue && item.TaskId.Value != Guid.Empty)
                {
                    // Parent is a pre-existing server-side task — validate ownership.
                    var parentTask = await _taskRepository.GetByIdUntrackedAsync(item.TaskId.Value, cancellationToken);
                    if (parentTask is not null && parentTask.UserId == userId && !parentTask.IsDeleted)
                    {
                        resolvedTaskId = item.TaskId.Value;
                    }
                }

                if (resolvedTaskId is null)
                {
                    results.Add(new SubtaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ParentNotFound,
                            Errors = ["Parent task not found or does not belong to the current user."]
                        }
                    });

                    continue;
                }

                var createResult = Subtask.Create(userId, resolvedTaskId.Value, item.Text, item.Position, utcNow);

                if (createResult.IsFailure)
                {
                    results.Add(new SubtaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var subtask = createResult.Value!;

                // Apply IsCompleted if set at creation
                if (item.IsCompleted)
                {
                    subtask.SetCompleted(true, utcNow);
                }

                // Create outbox message BEFORE persisting
                var payload = JsonSerializer.Serialize(new
                {
                    SubtaskId = subtask.Id,
                    subtask.UserId,
                    subtask.TaskId,
                    subtask.Text,
                    subtask.Version,
                    Event = SubtaskEventType.Created.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                    subtask,
                    SubtaskEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new SubtaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                await _subtaskRepository.AddAsync(subtask, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new SubtaskCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = subtask.Id,
                    Version = subtask.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        private async Task ProcessSubtaskUpdatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<SubtaskUpdatedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.Subtasks.Updated)
            {
                var subtask = await _subtaskRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (subtask is null || subtask.UserId != userId)
                {
                    results.Add(new SubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (subtask.IsDeleted)
                {
                    results.Add(new SubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (subtask.Version != item.ExpectedVersion)
                {
                    results.Add(new SubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = subtask.Version,
                            ServerSubtask = subtask.ToSyncDto()
                        }
                    });

                    continue;
                }

                // Apply only the fields the client sent (null = no change)
                var hasChanges = false;

                if (item.Text is not null)
                {
                    var textResult = subtask.UpdateText(item.Text, utcNow);
                    if (textResult.IsFailure)
                    {
                        results.Add(new SubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = textResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    hasChanges = true;
                }

                if (item.IsCompleted.HasValue)
                {
                    subtask.SetCompleted(item.IsCompleted.Value, utcNow);
                    hasChanges = true;
                }

                if (item.Position is not null)
                {
                    var posResult = subtask.UpdatePosition(item.Position, utcNow);
                    if (posResult.IsFailure)
                    {
                        results.Add(new SubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = posResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    hasChanges = true;
                }

                if (hasChanges)
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        SubtaskId = subtask.Id,
                        subtask.UserId,
                        subtask.TaskId,
                        subtask.Text,
                        subtask.Version,
                        Event = SubtaskEventType.Updated.ToString(),
                        OccurredAtUtc = utcNow,
                        OriginDeviceId = request.DeviceId
                    });

                    var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                        subtask,
                        SubtaskEventType.Updated,
                        payload,
                        utcNow);

                    if (outboxResult.IsFailure)
                    {
                        results.Add(new SubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.OutboxFailed,
                                Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    _subtaskRepository.Update(subtask);
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }

                results.Add(new SubtaskUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = subtask.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        private async Task ProcessSubtaskDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<SubtaskDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.Subtasks.Deleted)
            {
                var subtask = await _subtaskRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (subtask is null || subtask.UserId != userId)
                {
                    results.Add(new SubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (subtask.IsDeleted)
                {
                    // Idempotent: already deleted is acceptable
                    results.Add(new SubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                // Create outbox message BEFORE modifying the entity
                var payload = JsonSerializer.Serialize(new
                {
                    SubtaskId = subtask.Id,
                    subtask.UserId,
                    subtask.TaskId,
                    subtask.Text,
                    subtask.Version,
                    Event = SubtaskEventType.Deleted.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<Subtask, SubtaskEventType>(
                    subtask,
                    SubtaskEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new SubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Outbox created successfully — now safe to soft-delete
                var deleteResult = subtask.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new SubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _subtaskRepository.Update(subtask);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new SubtaskDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        // REFACTORED: added ProcessAttachmentDeletesAsync for task-attachments feature
        /// <summary>
        /// Processes attachment delete operations from the client push payload.
        /// Mirrors ProcessSubtaskDeletesAsync — delete-wins semantics, no version check.
        /// Blob deletion is deferred to the orphan-cleanup background worker.
        /// </summary>
        private async Task ProcessAttachmentDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<AttachmentDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.Attachments.Deleted)
            {
                var attachment = await _attachmentRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (attachment is null || attachment.UserId != userId)
                {
                    results.Add(new AttachmentDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (attachment.IsDeleted)
                {
                    // Idempotent: already deleted is acceptable
                    results.Add(new AttachmentDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                // Create outbox message BEFORE modifying the entity
                var payload = OutboxPayloadBuilder.BuildAttachmentPayload(attachment, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Attachment, AttachmentEventType>(
                    attachment,
                    AttachmentEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new AttachmentDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // Outbox created successfully — now safe to soft-delete
                var deleteResult = attachment.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new AttachmentDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _attachmentRepository.Update(attachment);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new AttachmentDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }


        // -----------------------------------------------
        // REFACTORED: Recurring Task processing (recurring-tasks feature)
        // Processing order: RecurringRoots → RecurringSeries → RecurringSeriesSubtasks
        //                   → RecurringExceptions  (inserted before Tasks in Handle)
        // No server-side cascade on deletes — the mobile client sends explicit deletes
        // for all related entities in the same push payload.
        // -----------------------------------------------

        /// <summary>
        /// Creates new RecurringTaskRoot entities from the push payload.
        /// Populates <paramref name="rootClientToServerIds"/> so subsequent series creates
        /// can resolve RootClientId references to within-push roots.
        /// </summary>
        private async Task ProcessRecurringRootCreatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringRootCreatedPushResultDto> results,
            Dictionary<Guid, Guid> rootClientToServerIds,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringRoots.Created)
            {
                var createResult = RecurringTaskRoot.Create(userId, utcNow);

                if (createResult.IsFailure)
                {
                    results.Add(new RecurringRootCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var root = createResult.Value;

                var payload = JsonSerializer.Serialize(new
                {
                    RootId = root.Id,
                    root.UserId,
                    root.Version,
                    Event = RecurringRootEventType.Created.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskRoot, RecurringRootEventType>(
                    root,
                    RecurringRootEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringRootCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                await _recurringRootRepository.AddAsync(root, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                rootClientToServerIds[item.ClientId] = root.Id;

                results.Add(new RecurringRootCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = root.Id,
                    Version = root.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        /// <summary>
        /// Soft-deletes RecurringTaskRoot entities from the push payload.
        /// Uses "delete wins" semantics — no server-side cascade to series, exceptions, or tasks.
        /// The mobile client sends explicit deletes for all related entities in the same push.
        /// </summary>
        private async Task ProcessRecurringRootDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringRootDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringRoots.Deleted)
            {
                var root = await _recurringRootRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (root is null || root.UserId != userId)
                {
                    results.Add(new RecurringRootDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (root.IsDeleted)
                {
                    results.Add(new RecurringRootDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    RootId = root.Id,
                    root.UserId,
                    root.Version,
                    Event = RecurringRootEventType.Deleted.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskRoot, RecurringRootEventType>(
                    root,
                    RecurringRootEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringRootDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var deleteResult = root.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new RecurringRootDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringRootRepository.Update(root);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringRootDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        /// <summary>
        /// Creates new RecurringTaskSeries entities from the push payload.
        /// Populates <paramref name="seriesClientToServerIds"/> so subsequent subtask creates
        /// and task creates can resolve within-push series references.
        /// MaterializedUpToDate is initialised to StartsOnDate − 1 day so the horizon worker
        /// advances it after the mobile's pushed TaskItems have been stored.
        /// </summary>
        private async Task ProcessRecurringSeriesCreatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSeriesCreatedPushResultDto> results,
            Dictionary<Guid, Guid> rootClientToServerIds,
            Dictionary<Guid, Guid> seriesClientToServerIds,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeries.Created)
            {
                // Resolve RootId: prefer within-push mapping, fall back to direct server ID.
                Guid? resolvedRootId = null;

                if (item.RootClientId.HasValue && item.RootClientId.Value != Guid.Empty)
                {
                    if (rootClientToServerIds.TryGetValue(item.RootClientId.Value, out var mappedRootId)
                        && mappedRootId != Guid.Empty)
                    {
                        resolvedRootId = mappedRootId;
                    }
                }
                else if (item.RootId.HasValue && item.RootId.Value != Guid.Empty)
                {
                    var root = await _recurringRootRepository.GetByIdUntrackedAsync(
                        item.RootId.Value, cancellationToken);
                    if (root is not null && root.UserId == userId && !root.IsDeleted)
                    {
                        resolvedRootId = item.RootId.Value;
                    }
                }

                if (resolvedRootId is null)
                {
                    results.Add(new RecurringSeriesCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ParentNotFound,
                            Errors = new[] { "Parent recurring root not found or does not belong to you." }
                        }
                    });

                    continue;
                }

                // MaterializedUpToDate = StartsOnDate − 1 day.
                // The mobile pushes individual Task.Created items for its already-materialized
                // occurrences (with RecurringSeriesId set), so the horizon worker will find those
                // existing tasks via GetBySeriesInRangeAsync and skip re-materialising them.
                var materializedUpToDate = item.StartsOnDate.AddDays(-1);

                var createResult = RecurringTaskSeries.Create(
                    userId,
                    resolvedRootId.Value,
                    item.RRuleString,
                    item.StartsOnDate,
                    item.EndsBeforeDate,
                    item.Title,
                    item.Description,
                    item.StartTime,
                    item.EndTime,
                    item.Location,
                    item.TravelTime,
                    item.CategoryId,
                    item.Priority,
                    item.MeetingLink,
                    item.ReminderOffsetMinutes,
                    materializedUpToDate,
                    utcNow);

                if (createResult.IsFailure)
                {
                    results.Add(new RecurringSeriesCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var series = createResult.Value;

                var payload = JsonSerializer.Serialize(new
                {
                    SeriesId = series.Id,
                    series.UserId,
                    series.RootId,
                    series.Version,
                    Event = RecurringSeriesEventType.Created.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskSeries, RecurringSeriesEventType>(
                    series,
                    RecurringSeriesEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringSeriesCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                await _recurringSeriesRepository.AddAsync(series, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                seriesClientToServerIds[item.ClientId] = series.Id;

                results.Add(new RecurringSeriesCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = series.Id,
                    Version = series.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        /// <summary>
        /// Applies template-field updates (and optional termination) to existing RecurringTaskSeries entities.
        /// When <see cref="RecurringSeriesUpdatedPushItemDto.EndsBeforeDate"/> is set,
        /// <see cref="RecurringTaskSeries.Terminate"/> is called and the outbox event is Terminated.
        /// </summary>
        private async Task ProcessRecurringSeriesUpdatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSeriesUpdatedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeries.Updated)
            {
                var series = await _recurringSeriesRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (series is null || series.UserId != userId)
                {
                    results.Add(new RecurringSeriesUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (series.IsDeleted)
                {
                    results.Add(new RecurringSeriesUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (series.Version != item.ExpectedVersion)
                {
                    results.Add(new RecurringSeriesUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = series.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = series.Version
                        }
                    });

                    continue;
                }

                var updateResult = series.UpdateTemplate(
                    item.Title,
                    item.Description,
                    item.StartTime,
                    item.EndTime,
                    item.Location,
                    item.TravelTime,
                    item.CategoryId,
                    item.Priority,
                    item.MeetingLink,
                    item.ReminderOffsetMinutes,
                    utcNow);

                if (updateResult.IsFailure)
                {
                    results.Add(new RecurringSeriesUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = series.Version,
                            Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // When EndsBeforeDate is provided, terminate the series segment (ThisAndFollowing split).
                var eventType = RecurringSeriesEventType.Updated;
                if (item.EndsBeforeDate.HasValue)
                {
                    var terminateResult = series.Terminate(item.EndsBeforeDate.Value, utcNow);
                    if (terminateResult.IsFailure)
                    {
                        results.Add(new RecurringSeriesUpdatedPushResultDto
                        {
                            Id = item.Id,
                            NewVersion = null,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = terminateResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    eventType = RecurringSeriesEventType.Terminated;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    SeriesId = series.Id,
                    series.UserId,
                    series.RootId,
                    series.Version,
                    Event = eventType.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskSeries, RecurringSeriesEventType>(
                    series,
                    eventType,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringSeriesUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringSeriesRepository.Update(series);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringSeriesUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = series.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        /// <summary>
        /// Soft-deletes RecurringTaskSeries entities from the push payload.
        /// Uses RecurringSeriesEventType.Terminated as the outbox event (series has no Deleted event type).
        /// No server-side cascade — the client sends explicit deletes for related subtasks,
        /// exceptions, and tasks in the same payload.
        /// </summary>
        private async Task ProcessRecurringSeriesDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSeriesDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeries.Deleted)
            {
                var series = await _recurringSeriesRepository.GetByIdUntrackedAsync(item.Id, cancellationToken);

                if (series is null || series.UserId != userId)
                {
                    results.Add(new RecurringSeriesDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (series.IsDeleted)
                {
                    results.Add(new RecurringSeriesDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    SeriesId = series.Id,
                    series.UserId,
                    series.RootId,
                    series.Version,
                    Event = RecurringSeriesEventType.Terminated.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskSeries, RecurringSeriesEventType>(
                    series,
                    RecurringSeriesEventType.Terminated,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringSeriesDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var deleteResult = series.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new RecurringSeriesDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringSeriesRepository.Update(series);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringSeriesDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        /// <summary>
        /// Creates new RecurringTaskSubtask entities (series template or exception override)
        /// from the push payload.
        /// Routing: ExceptionId set → CreateForException; otherwise → CreateForSeries.
        /// SeriesClientId is resolved via <paramref name="seriesClientToServerIds"/>.
        /// </summary>
        private async Task ProcessRecurringSubtaskCreatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSubtaskCreatedPushResultDto> results,
            Dictionary<Guid, Guid> seriesClientToServerIds,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeriesSubtasks.Created)
            {
                DomainResult<RecurringTaskSubtask> createResult;

                if (item.ExceptionId.HasValue && item.ExceptionId.Value != Guid.Empty)
                {
                    // Exception subtask override — validate exception ownership.
                    var exception = await _recurringExceptionRepository.GetByIdUntrackedAsync(
                        item.ExceptionId.Value, cancellationToken);

                    if (exception is null || exception.UserId != userId || exception.IsDeleted)
                    {
                        results.Add(new RecurringSubtaskCreatedPushResultDto
                        {
                            ClientId = item.ClientId,
                            Status = SyncPushCreatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ParentNotFound,
                                Errors = new[] { "Parent recurring exception not found or does not belong to you." }
                            }
                        });

                        continue;
                    }

                    createResult = RecurringTaskSubtask.CreateForException(
                        userId, item.ExceptionId.Value, item.Text, item.Position, item.IsCompleted, utcNow);
                }
                else
                {
                    // Series template subtask — resolve SeriesId.
                    Guid? resolvedSeriesId = null;

                    if (item.SeriesClientId.HasValue && item.SeriesClientId.Value != Guid.Empty)
                    {
                        if (seriesClientToServerIds.TryGetValue(item.SeriesClientId.Value, out var mapped)
                            && mapped != Guid.Empty)
                        {
                            resolvedSeriesId = mapped;
                        }
                    }
                    else if (item.SeriesId.HasValue && item.SeriesId.Value != Guid.Empty)
                    {
                        var series = await _recurringSeriesRepository.GetByIdUntrackedAsync(
                            item.SeriesId.Value, cancellationToken);
                        if (series is not null && series.UserId == userId && !series.IsDeleted)
                        {
                            resolvedSeriesId = item.SeriesId.Value;
                        }
                    }

                    if (resolvedSeriesId is null)
                    {
                        results.Add(new RecurringSubtaskCreatedPushResultDto
                        {
                            ClientId = item.ClientId,
                            Status = SyncPushCreatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ParentNotFound,
                                Errors = new[] { "Parent recurring series not found or does not belong to you." }
                            }
                        });

                        continue;
                    }

                    createResult = RecurringTaskSubtask.CreateForSeries(
                        userId, resolvedSeriesId.Value, item.Text, item.Position, utcNow);
                }

                if (createResult.IsFailure)
                {
                    results.Add(new RecurringSubtaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var subtask = createResult.Value;

                var payload = JsonSerializer.Serialize(new
                {
                    SubtaskId = subtask.Id,
                    subtask.UserId,
                    subtask.SeriesId,
                    subtask.ExceptionId,
                    subtask.Text,
                    subtask.Version,
                    Event = RecurringSeriesSubtaskEventType.Created.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskSubtask, RecurringSeriesSubtaskEventType>(
                    subtask,
                    RecurringSeriesSubtaskEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringSubtaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                await _recurringSeriesSubtaskRepository.AddAsync(subtask, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringSubtaskCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = subtask.Id,
                    Version = subtask.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        /// <summary>
        /// Applies partial updates (text, position, completion) to existing RecurringTaskSubtask entities.
        /// Null fields mean "no change" — same pattern as SubtaskUpdatedPushItemDto.
        /// </summary>
        private async Task ProcessRecurringSubtaskUpdatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSubtaskUpdatedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeriesSubtasks.Updated)
            {
                var subtask = await _recurringSeriesSubtaskRepository.GetByIdUntrackedAsync(
                    item.Id, cancellationToken);

                if (subtask is null || subtask.UserId != userId)
                {
                    results.Add(new RecurringSubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (subtask.IsDeleted)
                {
                    results.Add(new RecurringSubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (subtask.Version != item.ExpectedVersion)
                {
                    results.Add(new RecurringSubtaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = subtask.Version
                        }
                    });

                    continue;
                }

                var hasChanges = false;

                if (item.Text is not null)
                {
                    var textResult = subtask.UpdateText(item.Text, utcNow);
                    if (textResult.IsFailure)
                    {
                        results.Add(new RecurringSubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = textResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    hasChanges = true;
                }

                if (item.IsCompleted.HasValue)
                {
                    subtask.SetCompleted(item.IsCompleted.Value, utcNow);
                    hasChanges = true;
                }

                if (item.Position is not null)
                {
                    var posResult = subtask.UpdatePosition(item.Position, utcNow);
                    if (posResult.IsFailure)
                    {
                        results.Add(new RecurringSubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.ValidationFailed,
                                Errors = posResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    hasChanges = true;
                }

                if (hasChanges)
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        SubtaskId = subtask.Id,
                        subtask.UserId,
                        subtask.SeriesId,
                        subtask.ExceptionId,
                        subtask.Text,
                        subtask.Version,
                        Event = RecurringSeriesSubtaskEventType.Updated.ToString(),
                        OccurredAtUtc = utcNow,
                        OriginDeviceId = request.DeviceId
                    });

                    var outboxResult = OutboxMessage.Create<RecurringTaskSubtask, RecurringSeriesSubtaskEventType>(
                        subtask,
                        RecurringSeriesSubtaskEventType.Updated,
                        payload,
                        utcNow);

                    if (outboxResult.IsFailure)
                    {
                        results.Add(new RecurringSubtaskUpdatedPushResultDto
                        {
                            Id = item.Id,
                            Status = SyncPushUpdatedStatus.Failed,
                            Conflict = new SyncPushConflictDetailDto
                            {
                                ConflictType = SyncConflictType.OutboxFailed,
                                Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                            }
                        });

                        continue;
                    }

                    _recurringSeriesSubtaskRepository.Update(subtask);
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }

                results.Add(new RecurringSubtaskUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = subtask.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        /// <summary>
        /// Soft-deletes RecurringTaskSubtask entities from the push payload.
        /// Covers both series template subtasks and exception subtask overrides.
        /// </summary>
        private async Task ProcessRecurringSubtaskDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringSubtaskDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringSeriesSubtasks.Deleted)
            {
                var subtask = await _recurringSeriesSubtaskRepository.GetByIdUntrackedAsync(
                    item.Id, cancellationToken);

                if (subtask is null || subtask.UserId != userId)
                {
                    results.Add(new RecurringSubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (subtask.IsDeleted)
                {
                    results.Add(new RecurringSubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    SubtaskId = subtask.Id,
                    subtask.UserId,
                    subtask.SeriesId,
                    subtask.ExceptionId,
                    subtask.Text,
                    subtask.Version,
                    Event = RecurringSeriesSubtaskEventType.Deleted.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskSubtask, RecurringSeriesSubtaskEventType>(
                    subtask,
                    RecurringSeriesSubtaskEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringSubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var deleteResult = subtask.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new RecurringSubtaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringSeriesSubtaskRepository.Update(subtask);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringSubtaskDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }

        /// <summary>
        /// Upserts RecurringTaskException entities from the push payload.
        /// If an active exception already exists for (SeriesId, OccurrenceDate), it is soft-deleted
        /// and replaced by a new one so the DB unique index constraint is satisfied within the same
        /// SaveChangesAsync call. The outbox event is always Created (representing the final desired state).
        /// </summary>
        private async Task ProcessRecurringExceptionCreatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringExceptionCreatedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringExceptions.Created)
            {
                // Verify series ownership.
                var series = await _recurringSeriesRepository.GetByIdUntrackedAsync(
                    item.SeriesId, cancellationToken);

                if (series is null || series.UserId != userId)
                {
                    results.Add(new RecurringExceptionCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ParentNotFound,
                            Errors = new[] { "Parent recurring series not found or does not belong to you." }
                        }
                    });

                    continue;
                }

                // Create the new exception domain object.
                DomainResult<RecurringTaskException> createResult;

                if (item.IsDeletion)
                {
                    createResult = RecurringTaskException.CreateDeletion(
                        userId, item.SeriesId, item.OccurrenceDate,
                        materializedTaskItemId: null, utcNow);
                }
                else
                {
                    createResult = RecurringTaskException.CreateOverride(
                        userId, item.SeriesId, item.OccurrenceDate,
                        item.OverrideTitle,
                        item.OverrideDescription,
                        item.OverrideDate,
                        item.OverrideStartTime,
                        item.OverrideEndTime,
                        item.OverrideLocation,
                        item.OverrideTravelTime,
                        item.OverrideCategoryId,
                        item.OverridePriority,
                        item.OverrideMeetingLink,
                        item.OverrideReminderAtUtc,
                        isCompleted: item.IsCompleted,
                        materializedTaskItemId: null,
                        utcNow);
                }

                if (createResult.IsFailure)
                {
                    results.Add(new RecurringExceptionCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = createResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var newException = createResult.Value;

                // Build outbox BEFORE modifying the change tracker (consistent pattern).
                var payload = JsonSerializer.Serialize(new
                {
                    ExceptionId = newException.Id,
                    newException.UserId,
                    newException.SeriesId,
                    newException.OccurrenceDate,
                    newException.IsDeletion,
                    newException.Version,
                    Event = RecurringExceptionEventType.Created.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskException, RecurringExceptionEventType>(
                    newException,
                    RecurringExceptionEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringExceptionCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = SyncPushCreatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                // If an active exception already exists for this (SeriesId, OccurrenceDate),
                // soft-delete it to satisfy the unique DB index. Both the soft-delete and the
                // new insert are committed in the same SaveChangesAsync() transaction.
                var existing = await _recurringExceptionRepository.GetByOccurrenceAsync(
                    item.SeriesId, item.OccurrenceDate, cancellationToken);

                if (existing is not null && !existing.IsDeleted)
                {
                    existing.SoftDelete(utcNow);
                    _recurringExceptionRepository.Update(existing);
                }

                await _recurringExceptionRepository.AddAsync(newException, cancellationToken);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringExceptionCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = newException.Id,
                    Version = newException.Version,
                    Status = SyncPushCreatedStatus.Created
                });
            }
        }

        /// <summary>
        /// Applies override field updates to existing RecurringTaskException entities.
        /// UpdateOverride() fails if the exception is a deletion tombstone (IsDeletion = true) —
        /// that error is surfaced as a ValidationFailed conflict.
        /// </summary>
        private async Task ProcessRecurringExceptionUpdatesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringExceptionUpdatedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringExceptions.Updated)
            {
                var exception = await _recurringExceptionRepository.GetByIdUntrackedAsync(
                    item.Id, cancellationToken);

                if (exception is null || exception.UserId != userId)
                {
                    results.Add(new RecurringExceptionUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.NotFound
                        }
                    });

                    continue;
                }

                if (exception.IsDeleted)
                {
                    results.Add(new RecurringExceptionUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.DeletedOnServer
                        }
                    });

                    continue;
                }

                if (exception.Version != item.ExpectedVersion)
                {
                    results.Add(new RecurringExceptionUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = exception.Version,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.VersionMismatch,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = exception.Version
                        }
                    });

                    continue;
                }

                var updateResult = exception.UpdateOverride(
                    item.OverrideTitle,
                    item.OverrideDescription,
                    item.OverrideDate,
                    item.OverrideStartTime,
                    item.OverrideEndTime,
                    item.OverrideLocation,
                    item.OverrideTravelTime,
                    item.OverrideCategoryId,
                    item.OverridePriority,
                    item.OverrideMeetingLink,
                    item.OverrideReminderAtUtc,
                    isCompleted: item.IsCompleted ?? exception.IsCompleted,
                    utcNow);

                if (updateResult.IsFailure)
                {
                    results.Add(new RecurringExceptionUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            ClientVersion = item.ExpectedVersion,
                            ServerVersion = exception.Version,
                            Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    ExceptionId = exception.Id,
                    exception.UserId,
                    exception.SeriesId,
                    exception.OccurrenceDate,
                    exception.Version,
                    Event = RecurringExceptionEventType.Updated.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskException, RecurringExceptionEventType>(
                    exception,
                    RecurringExceptionEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringExceptionUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringExceptionRepository.Update(exception);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringExceptionUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = exception.Version,
                    Status = SyncPushUpdatedStatus.Updated
                });
            }
        }

        /// <summary>
        /// Soft-deletes RecurringTaskException entities from the push payload.
        /// Mirrors ProcessSubtaskDeletesAsync — delete-wins semantics.
        /// </summary>
        private async Task ProcessRecurringExceptionDeletesAsync(
            Guid userId,
            SyncPushCommand request,
            DateTime utcNow,
            List<RecurringExceptionDeletedPushResultDto> results,
            CancellationToken cancellationToken)
        {
            foreach (var item in request.RecurringExceptions.Deleted)
            {
                var exception = await _recurringExceptionRepository.GetByIdUntrackedAsync(
                    item.Id, cancellationToken);

                if (exception is null || exception.UserId != userId)
                {
                    results.Add(new RecurringExceptionDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.NotFound
                    });

                    continue;
                }

                if (exception.IsDeleted)
                {
                    results.Add(new RecurringExceptionDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.AlreadyDeleted
                    });

                    continue;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    ExceptionId = exception.Id,
                    exception.UserId,
                    exception.SeriesId,
                    exception.OccurrenceDate,
                    exception.Version,
                    Event = RecurringExceptionEventType.Deleted.ToString(),
                    OccurredAtUtc = utcNow,
                    OriginDeviceId = request.DeviceId
                });

                var outboxResult = OutboxMessage.Create<RecurringTaskException, RecurringExceptionEventType>(
                    exception,
                    RecurringExceptionEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsFailure)
                {
                    results.Add(new RecurringExceptionDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.OutboxFailed,
                            Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                var deleteResult = exception.SoftDelete(utcNow);
                if (deleteResult.IsFailure)
                {
                    results.Add(new RecurringExceptionDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = SyncPushDeletedStatus.Failed,
                        Conflict = new SyncPushConflictDetailDto
                        {
                            ConflictType = SyncConflictType.ValidationFailed,
                            Errors = deleteResult.Errors.Select(e => e.Message).ToArray()
                        }
                    });

                    continue;
                }

                _recurringExceptionRepository.Update(exception);
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

                results.Add(new RecurringExceptionDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = SyncPushDeletedStatus.Deleted
                });
            }
        }
    }
}
