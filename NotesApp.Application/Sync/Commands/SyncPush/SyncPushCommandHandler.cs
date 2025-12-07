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
    /// - Applies creates/updates/deletes for tasks and notes.
    /// - Uses Version for optimistic concurrency on updates.
    /// - Always uses "delete wins" semantics for deletes.
    /// - Emits outbox messages for Created / Updated / Deleted events.
    /// 
    /// Per-item conflicts (version mismatch, not found, etc.) are collected and
    /// returned in the result rather than causing the whole command to fail.
    /// </summary>
    public sealed class SyncPushCommandHandler
        : IRequestHandler<SyncPushCommand, Result<SyncPushResultDto>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<SyncPushCommandHandler> _logger;

        public SyncPushCommandHandler(ICurrentUserService currentUserService,
                                      ITaskRepository taskRepository,
                                      INoteRepository noteRepository,
                                      IUserDeviceRepository deviceRepository,
                                      IOutboxRepository outboxRepository,
                                      IUnitOfWork unitOfWork,
                                      ISystemClock clock,
                                      ILogger<SyncPushCommandHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _deviceRepository = deviceRepository;
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

            // NEW: device ownership / status check
            var device = await _deviceRepository.GetByIdAsync(request.DeviceId, cancellationToken);
            if (device is null ||
                device.UserId != userId ||
                !device.IsActive ||
                device.IsDeleted)
            {
                return Result.Fail(new Error("Device.NotFound"));
            }

            _logger.LogInformation(
                "Sync push received from device {DeviceId} for user {UserId} at {UtcNow}",
                request.DeviceId,
                userId,
                utcNow);

            var taskCreateResults = new List<TaskCreatedPushResultDto>();
            var taskUpdateResults = new List<TaskUpdatedPushResultDto>();
            var taskDeleteResults = new List<TaskDeletedPushResultDto>();

            var noteCreateResults = new List<NoteCreatedPushResultDto>();
            var noteUpdateResults = new List<NoteUpdatedPushResultDto>();
            var noteDeleteResults = new List<NoteDeletedPushResultDto>();

            var conflicts = new List<SyncConflictDto>();

            // Process tasks
            await ProcessTaskCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskCreateResults,
                                          conflicts,
                                          cancellationToken);

            await ProcessTaskUpdatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskUpdateResults,
                                          conflicts,
                                          cancellationToken);

            await ProcessTaskDeletesAsync(userId,
                                          request,
                                          utcNow,
                                          taskDeleteResults,
                                          conflicts,
                                          cancellationToken);

            // Process notes
            await ProcessNoteCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          noteCreateResults,
                                          conflicts,
                                          cancellationToken);

            await ProcessNoteUpdatesAsync(userId,
                                          request,
                                          utcNow,
                                          noteUpdateResults,
                                          conflicts,
                                          cancellationToken);

            await ProcessNoteDeletesAsync(userId,
                                          request,
                                          utcNow,
                                          noteDeleteResults,
                                          conflicts,
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
                Conflicts = conflicts
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
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Created)
            {
                var createResult = TaskItem.Create(userId,
                                                   item.Date,
                                                   item.Title,
                                                   item.Description,
                                                   item.StartTime,
                                                   item.EndTime,
                                                   item.Location,
                                                   item.TravelTime,
                                                   utcNow);

                if (createResult.IsFailure)
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = null,
                        ConflictType = "validation_failed",
                        Errors = createResult.Errors.Select(e => e.Message).ToArray()
                    });

                    results.Add(new TaskCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = "failed"
                    });

                    continue;
                }

                var task = createResult.Value;

                if (item.ReminderAtUtc is not null)
                {
                    task.SetReminder(item.ReminderAtUtc.Value, utcNow);
                }

                await _taskRepository.AddAsync(task, cancellationToken);

                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);

                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = task.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new TaskCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = task.Id,
                    Version = task.Version,
                    Status = "created"
                });
            }
        }

        private async Task ProcessTaskUpdatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<TaskUpdatedPushResultDto> results,
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Updated)
            {
                var task = await _taskRepository.GetByIdAsync(item.Id, cancellationToken);

                if (task is null)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = "not_found"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = item.Id,
                        ConflictType = "not_found"
                    });

                    continue;
                }

                if (task.IsDeleted)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = "deleted_on_server"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = item.Id,
                        ConflictType = "deleted_on_server"
                    });

                    continue;
                }

                if (task.Version != item.ExpectedVersion)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = task.Version,
                        Status = "conflict"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = item.Id,
                        ConflictType = "version_mismatch",
                        ClientVersion = item.ExpectedVersion,
                        ServerVersion = task.Version,
                        ServerTask = task.ToSyncDto()
                    });

                    continue;
                }

                var updateResult = task.Update(
                    item.Title,
                    item.Date,
                    item.Description,
                    item.StartTime,
                    item.EndTime,
                    item.Location,
                    item.TravelTime,
                    utcNow);

                if (updateResult.IsFailure)
                {
                    results.Add(new TaskUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = task.Version,
                        Status = "validation_failed"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = item.Id,
                        ConflictType = "validation_failed",
                        ClientVersion = item.ExpectedVersion,
                        ServerVersion = task.Version,
                        Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                    });

                    continue;
                }

                if (item.ReminderAtUtc is not null)
                {
                    task.SetReminder(item.ReminderAtUtc.Value, utcNow);
                }
                else
                {
                    task.SetReminder(null, utcNow);
                }

                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);

                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = task.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new TaskUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = task.Version,
                    Status = "updated"
                });
            }
        }

        private async Task ProcessTaskDeletesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<TaskDeletedPushResultDto> results,
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Tasks.Deleted)
            {
                var task = await _taskRepository.GetByIdAsync(item.Id, cancellationToken);

                if (task is null)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = "not_found"
                    });

                    continue;
                }

                if (task.IsDeleted)
                {
                    results.Add(new TaskDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = "already_deleted"
                    });

                    continue;
                }

                task.SoftDelete(utcNow);

                var payload = OutboxPayloadBuilder.BuildTaskPayload(task, request.DeviceId);

                var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(
                    task,
                    TaskEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "task",
                        EntityId = task.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new TaskDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = "deleted"
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
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Created)
            {
                var createResult = Note.Create(
                    userId,
                    item.Date,
                    item.Title,
                    item.Content,
                    item.Summary,
                    item.Tags,
                    utcNow);

                if (createResult.IsFailure)
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = null,
                        ConflictType = "validation_failed",
                        Errors = createResult.Errors.Select(e => e.Message).ToArray()
                    });

                    results.Add(new NoteCreatedPushResultDto
                    {
                        ClientId = item.ClientId,
                        ServerId = Guid.Empty,
                        Version = 0,
                        Status = "failed"
                    });

                    continue;
                }

                var note = createResult.Value;
                await _noteRepository.AddAsync(note, cancellationToken);

                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                    note,
                    NoteEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = note.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new NoteCreatedPushResultDto
                {
                    ClientId = item.ClientId,
                    ServerId = note.Id,
                    Version = note.Version,
                    Status = "created"
                });
            }
        }

        private async Task ProcessNoteUpdatesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<NoteUpdatedPushResultDto> results,
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Updated)
            {
                var note = await _noteRepository.GetByIdAsync(item.Id, cancellationToken);

                if (note is null)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = "not_found"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = item.Id,
                        ConflictType = "not_found"
                    });

                    continue;
                }

                if (note.IsDeleted)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = "deleted_on_server"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = item.Id,
                        ConflictType = "deleted_on_server"
                    });

                    continue;
                }

                if (note.Version != item.ExpectedVersion)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = note.Version,
                        Status = "conflict"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = item.Id,
                        ConflictType = "version_mismatch",
                        ClientVersion = item.ExpectedVersion,
                        ServerVersion = note.Version,
                        ServerNote = note.ToSyncDto()
                    });

                    continue;
                }

                var updateResult = note.Update(item.Title,
                                               item.Content,
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
                        Status = "validation_failed"
                    });

                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = item.Id,
                        ConflictType = "validation_failed",
                        ClientVersion = item.ExpectedVersion,
                        ServerVersion = note.Version,
                        Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                    });

                    continue;
                }

                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                    note,
                    NoteEventType.Updated,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = note.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new NoteUpdatedPushResultDto
                {
                    Id = item.Id,
                    NewVersion = note.Version,
                    Status = "updated"
                });
            }
        }

        private async Task ProcessNoteDeletesAsync(Guid userId,
                                                   SyncPushCommand request,
                                                   DateTime utcNow,
                                                   List<NoteDeletedPushResultDto> results,
                                                   List<SyncConflictDto> conflicts,
                                                   CancellationToken cancellationToken)
        {
            foreach (var item in request.Notes.Deleted)
            {
                var note = await _noteRepository.GetByIdAsync(item.Id, cancellationToken);

                if (note is null)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = "not_found"
                    });

                    continue;
                }

                if (note.IsDeleted)
                {
                    results.Add(new NoteDeletedPushResultDto
                    {
                        Id = item.Id,
                        Status = "already_deleted"
                    });

                    continue;
                }

                note.SoftDelete(utcNow);

                var payload = OutboxPayloadBuilder.BuildNotePayload(note, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Note, NoteEventType>(
                    note,
                    NoteEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    conflicts.Add(new SyncConflictDto
                    {
                        EntityType = "note",
                        EntityId = note.Id,
                        ConflictType = "outbox_failed",
                        Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                    });
                }

                results.Add(new NoteDeletedPushResultDto
                {
                    Id = item.Id,
                    Status = "deleted"
                });
            }
        }

    }
}
