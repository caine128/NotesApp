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
    /// - Applies creates/updates/deletes for tasks, notes, and blocks.
    /// - Uses Version for optimistic concurrency on updates.
    /// - Always uses "delete wins" semantics for deletes.
    /// - Emits outbox messages for Created / Updated / Deleted events.
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
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<SyncPushCommandHandler> _logger;

        public SyncPushCommandHandler(ICurrentUserService currentUserService,
                                     ITaskRepository taskRepository,
                                     INoteRepository noteRepository,
                                     IBlockRepository blockRepository,
                                     IUserDeviceRepository deviceRepository,
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

            var blockCreateResults = new List<BlockCreatedPushResultDto>();
            var blockUpdateResults = new List<BlockUpdatedPushResultDto>();
            var blockDeleteResults = new List<BlockDeletedPushResultDto>();

            // Maps client IDs to server IDs for entities created in this push.
            // Used to resolve parent references for blocks.
            var taskClientToServerIds = new Dictionary<Guid, Guid>();
            var noteClientToServerIds = new Dictionary<Guid, Guid>();

            // Process tasks (collect client->server ID mappings)
            await ProcessTaskCreatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskCreateResults,
                                          taskClientToServerIds,
                                          cancellationToken);

            await ProcessTaskUpdatesAsync(userId,
                                          request,
                                          utcNow,
                                          taskUpdateResults,
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
                    // Log but don't fail - entity was created successfully
                    _logger.LogWarning(
                        "Failed to create outbox message for task {TaskId}: {Errors}",
                        task.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                        Status = SyncPushUpdatedStatus.NotFound,
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
                        Status = SyncPushUpdatedStatus.DeletedOnServer,
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
                        Status = SyncPushUpdatedStatus.Conflict,
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

                var updateResult = task.Update(item.Title,
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
                        Status = SyncPushUpdatedStatus.ValidationFailed,
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
                    _logger.LogWarning(
                        "Failed to create outbox message for task update {TaskId}: {Errors}",
                        task.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                var task = await _taskRepository.GetByIdAsync(item.Id, cancellationToken);

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
                    _logger.LogWarning(
                        "Failed to create outbox message for task delete {TaskId}: {Errors}",
                        task.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                                               item.Content,
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
                    _logger.LogWarning(
                        "Failed to create outbox message for note {NoteId}: {Errors}",
                        note.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                var note = await _noteRepository.GetByIdAsync(item.Id, cancellationToken);

                if (note is null)
                {
                    results.Add(new NoteUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.NotFound,
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
                        Status = SyncPushUpdatedStatus.DeletedOnServer,
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
                        Status = SyncPushUpdatedStatus.Conflict,
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
                        Status = SyncPushUpdatedStatus.ValidationFailed,
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
                    _logger.LogWarning(
                        "Failed to create outbox message for note update {NoteId}: {Errors}",
                        note.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                var note = await _noteRepository.GetByIdAsync(item.Id, cancellationToken);

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
                    _logger.LogWarning(
                        "Failed to create outbox message for note delete {NoteId}: {Errors}",
                        note.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                await _blockRepository.AddAsync(block, cancellationToken);

                var payload = OutboxPayloadBuilder.BuildBlockPayload(block, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                    block,
                    BlockEventType.Created,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create outbox message for block {BlockId}: {Errors}",
                        block.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

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
                var block = await _blockRepository.GetByIdAsync(item.Id, cancellationToken);

                if (block is null)
                {
                    results.Add(new BlockUpdatedPushResultDto
                    {
                        Id = item.Id,
                        NewVersion = null,
                        Status = SyncPushUpdatedStatus.NotFound,
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
                        Status = SyncPushUpdatedStatus.NotFound,
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
                        Status = SyncPushUpdatedStatus.DeletedOnServer,
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
                        Status = SyncPushUpdatedStatus.Conflict,
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
                            Status = SyncPushUpdatedStatus.ValidationFailed,
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
                            Status = SyncPushUpdatedStatus.ValidationFailed,
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

                    if (outboxResult.IsSuccess)
                    {
                        await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to create outbox message for block update {BlockId}: {Errors}",
                            block.Id,
                            string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                    }
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
                var block = await _blockRepository.GetByIdAsync(item.Id, cancellationToken);

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

                block.SoftDelete(utcNow);

                var payload = OutboxPayloadBuilder.BuildBlockPayload(block, request.DeviceId);

                var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                    block,
                    BlockEventType.Deleted,
                    payload,
                    utcNow);

                if (outboxResult.IsSuccess)
                {
                    await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create outbox message for block delete {BlockId}: {Errors}",
                        block.Id,
                        string.Join(", ", outboxResult.Errors.Select(e => e.Message)));
                }

                results.Add(new BlockDeletedPushResultDto
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
        private static Guid? ResolveParentId(
            Guid? parentId,
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

            var mapping = parentType switch
            {
                BlockParentType.Task => taskClientToServerIds,
                BlockParentType.Note => noteClientToServerIds,
                _ => null
            };

            if (mapping is null)
            {
                return null;
            }

            return mapping.TryGetValue(parentClientId.Value, out var serverId)
                ? serverId
                : null;
        }
    }
}
