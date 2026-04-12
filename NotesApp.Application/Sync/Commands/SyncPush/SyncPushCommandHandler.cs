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

            // Maps client IDs to server IDs for entities created in this push.
            // Used to resolve parent references for blocks.
            var taskClientToServerIds = new Dictionary<Guid, Guid>();
            var noteClientToServerIds = new Dictionary<Guid, Guid>();

            // REFACTORED: categories must be processed BEFORE tasks so that within-push CategoryId
            // references (task.CategoryId pointing to a category ClientId from the same push) resolve
            // correctly via categoryClientToServerIds.
            var categoryClientToServerIds = new Dictionary<Guid, Guid>();

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

            // Process tasks (collect client->server ID mappings)
            // Pass categoryClientToServerIds so CategoryId can be resolved for within-push categories.
            await ProcessTaskCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskCreateResults,
                                          taskClientToServerIds,
                                          categoryClientToServerIds, // REFACTORED
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
    }
}
