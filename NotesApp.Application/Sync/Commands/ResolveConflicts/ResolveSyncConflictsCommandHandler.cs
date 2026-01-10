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

namespace NotesApp.Application.Sync.Commands.ResolveConflicts
{
    /// <summary>
    /// Applies client-chosen resolutions for previously reported sync conflicts.
    /// 
    /// Semantics:
    /// - keep_server: no changes are applied; the current server state wins.
    /// - keep_client / merge: client-provided data is applied as an update,
    ///   with ExpectedVersion used for optimistic concurrency.
    /// 
    /// Second-level conflicts (server changed again before resolution) are
    /// reported with Status = "conflict" and no changes applied.
    /// </summary>
    public sealed class ResolveSyncConflictsCommandHandler
        : IRequestHandler<ResolveSyncConflictsCommand, Result<ResolveSyncConflictsResultDto>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<ResolveSyncConflictsCommandHandler> _logger;

        public ResolveSyncConflictsCommandHandler(
            ICurrentUserService currentUserService,
            ITaskRepository taskRepository,
            INoteRepository noteRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            ILogger<ResolveSyncConflictsCommandHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<ResolveSyncConflictsResultDto>> Handle(ResolveSyncConflictsCommand request,
                                                                        CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            _logger.LogInformation("Resolving {Count} sync conflicts for user {UserId} at {UtcNow}",
                                   request.Request.Resolutions.Count,
                                   userId,
                                   utcNow);

            var results = new List<SyncConflictResolutionResultItemDto>();

            foreach (var resolution in request.Request.Resolutions)
            {
                if (resolution.EntityType == SyncEntityType.Task)
                {
                    var result = await ResolveTaskConflictAsync(userId, resolution, utcNow, cancellationToken);
                    results.Add(result);
                }
                else if (resolution.EntityType == SyncEntityType.Note)
                {
                    var result = await ResolveNoteConflictAsync(userId, resolution, utcNow, cancellationToken);
                    results.Add(result);
                }
                else
                {
                    results.Add(new SyncConflictResolutionResultItemDto
                    {
                        EntityType = resolution.EntityType,
                        EntityId = resolution.EntityId,
                        Status = SyncConflictResolutionStatus.InvalidEntityType,
                        NewVersion = null,
                        Errors = new[] { "Unsupported EntityType." }
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new ResolveSyncConflictsResultDto
            {
                Results = results
            };

            return Result.Ok(dto);
        }

        // --------------------------------------------------------------------
        // Task resolution
        // --------------------------------------------------------------------

        private async Task<SyncConflictResolutionResultItemDto> ResolveTaskConflictAsync(Guid userId,
                                                                                         SyncConflictResolutionDto resolution,
                                                                                         DateTime utcNow,
                                                                                         CancellationToken cancellationToken)
        {
            var task = await _taskRepository.GetByIdAsync(resolution.EntityId, cancellationToken);

            if (task is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.NotFound,
                    NewVersion = null,
                    Errors = Array.Empty<string>()
                };
            }

            if (task.IsDeleted)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.DeletedOnServer,
                    NewVersion = task.Version,
                    Errors = Array.Empty<string>()
                };
            }

            // keep_server: just acknowledge, no change
            if (resolution.Choice == SyncResolutionChoice.KeepServer)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.KeptServer,
                    NewVersion = task.Version,
                    Errors = Array.Empty<string>()
                };
            }

            // For keep_client / merge we expect TaskData and perform an update
            if (task.Version != resolution.ExpectedVersion)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.Conflict,
                    NewVersion = task.Version,
                    Errors = new[]
                    {
                        "Server version does not match ExpectedVersion. The entity has changed again on the server."
                    }
                };
            }

            if (resolution.TaskData is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = task.Version,
                    Errors = new[] { "TaskData must be provided for keep_client/merge resolutions." }
                };
            }

            var data = resolution.TaskData;

            var updateResult = task.Update(data.Title,
                                           data.Date,
                                           data.Description,
                                           data.StartTime,
                                           data.EndTime,
                                           data.Location,
                                           data.TravelTime,
                                           utcNow);

            if (updateResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = task.Version,
                    Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            if (data.ReminderAtUtc is not null)
            {
                task.SetReminder(data.ReminderAtUtc.Value, utcNow);
            }
            else
            {
                task.SetReminder(null, utcNow);
            }

            var payload = OutboxPayloadBuilder.BuildTaskPayload(task, Guid.Empty);

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(task,
                                                                             TaskEventType.Updated,
                                                                             payload,
                                                                             utcNow);

            if (outboxResult.IsSuccess)
            {
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            }

            return new SyncConflictResolutionResultItemDto
            {
                EntityType = SyncEntityType.Task,
                EntityId = resolution.EntityId,
                Status = SyncConflictResolutionStatus.Updated,
                NewVersion = task.Version,
                Errors = Array.Empty<string>()
            };
        }

        // --------------------------------------------------------------------
        // Note resolution
        // --------------------------------------------------------------------

        private async Task<SyncConflictResolutionResultItemDto> ResolveNoteConflictAsync(Guid userId,
                                                                                         SyncConflictResolutionDto resolution,
                                                                                         DateTime utcNow,
                                                                                         CancellationToken cancellationToken)
        {
            var note = await _noteRepository.GetByIdAsync(resolution.EntityId, cancellationToken);

            if (note is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.NotFound,
                    NewVersion = null,
                    Errors = Array.Empty<string>()
                };
            }

            if (note.IsDeleted)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.DeletedOnServer,
                    NewVersion = note.Version,
                    Errors = Array.Empty<string>()
                };
            }

            if (resolution.Choice == SyncResolutionChoice.KeepServer)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.KeptServer,
                    NewVersion = note.Version,
                    Errors = Array.Empty<string>()
                };
            }

            if (note.Version != resolution.ExpectedVersion)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.Conflict,
                    NewVersion = note.Version,
                    Errors = new[]
                    {
                        "Server version does not match ExpectedVersion. The entity has changed again on the server."
                    }
                };
            }

            if (resolution.NoteData is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = note.Version,
                    Errors = new[] { "NoteData must be provided for keep_client/merge resolutions." }
                };
            }

            var data = resolution.NoteData;

            var updateResult = note.Update(data.Title,
                                           data.Content,
                                           data.Summary,
                                           data.Tags,
                                           data.Date,
                                           utcNow);

            if (updateResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = note.Version,
                    Errors = updateResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            var payload = OutboxPayloadBuilder.BuildNotePayload(note, Guid.Empty);

            var outboxResult = OutboxMessage.Create<Note, NoteEventType>(note,
                                                                         NoteEventType.Updated,
                                                                         payload,
                                                                         utcNow);

            if (outboxResult.IsSuccess)
            {
                await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);
            }

            return new SyncConflictResolutionResultItemDto
            {
                EntityType = SyncEntityType.Note,
                EntityId = resolution.EntityId,
                Status = SyncConflictResolutionStatus.Updated,
                NewVersion = note.Version,
                Errors = Array.Empty<string>()
            };
        }

       
    }
}
