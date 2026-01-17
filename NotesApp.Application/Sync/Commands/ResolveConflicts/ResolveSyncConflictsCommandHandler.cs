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
    /// This handler is a FALLBACK for rare cases when the server state changes
    /// between the client's pull and push (race condition). In the normal flow:
    /// 1. Client pulls latest server state
    /// 2. Client resolves conflicts locally
    /// 3. Client pushes with correct ExpectedVersion
    /// 4. Server accepts without conflict
    /// 
    /// Semantics:
    /// - KeepServer: no changes are applied; the current server state wins.
    /// - KeepClient / Merge: client-provided data is applied as an update,
    ///   with ExpectedVersion used for optimistic concurrency.
    /// 
    /// Second-level conflicts (server changed again before resolution) are
    /// reported with Status = "Conflict" and no changes applied.
    /// 
    /// Each resolution is processed independently using the UNTRACKED PATTERN:
    /// - Entities are loaded WITHOUT tracking to prevent auto-persistence on failure.
    /// - ALL domain operations and outbox creation must succeed for an item to be persisted.
    /// - Individual item failures are reported without affecting other items.
    /// - Only fully successful items are persisted via explicit Update() calls.
    /// </summary>
    public sealed class ResolveSyncConflictsCommandHandler
        : IRequestHandler<ResolveSyncConflictsCommand, Result<ResolveSyncConflictsResultDto>>
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;  // ADDED: Block repository
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISystemClock _clock;
        private readonly ILogger<ResolveSyncConflictsCommandHandler> _logger;

        public ResolveSyncConflictsCommandHandler(
            ICurrentUserService currentUserService,
            ITaskRepository taskRepository,
            INoteRepository noteRepository,
            IBlockRepository blockRepository,  // ADDED: Block repository injection
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ISystemClock clock,
            ILogger<ResolveSyncConflictsCommandHandler> logger)
        {
            _currentUserService = currentUserService;
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;  // ADDED
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

            // Track successful resolutions that need to be persisted
            var pendingTaskUpdates = new List<(TaskItem Task, OutboxMessage Outbox)>();
            var pendingNoteUpdates = new List<(Note Note, OutboxMessage Outbox)>();

            foreach (var resolution in request.Request.Resolutions)
            {
                SyncConflictResolutionResultItemDto result = resolution.EntityType switch
                {
                    SyncEntityType.Task => await ResolveTaskConflictAsync(userId, resolution, utcNow, cancellationToken),
                    SyncEntityType.Note => await ResolveNoteConflictAsync(userId, resolution, utcNow, cancellationToken),
                    SyncEntityType.Block => await ResolveBlockConflictAsync(userId, resolution, utcNow, cancellationToken),  // ADDED
                    _ => new SyncConflictResolutionResultItemDto
                    {
                        EntityType = resolution.EntityType,
                        EntityId = resolution.EntityId,
                        Status = SyncConflictResolutionStatus.InvalidEntityType,
                        NewVersion = null,
                        Errors = new[] { $"Unsupported EntityType: {resolution.EntityType}." }
                    }
                };

                results.Add(result);
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
            var task = await _taskRepository.GetByIdUntrackedAsync(resolution.EntityId, cancellationToken);

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

            // Verify ownership (security boundary)
            if (task.UserId != userId)
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

            // Handle reminder separately
            var reminderResult = task.SetReminder(data.ReminderAtUtc, utcNow);
            if (reminderResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = task.Version,
                    Errors = reminderResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            // Create outbox message BEFORE persisting
            var payload = OutboxPayloadBuilder.BuildTaskPayload(task, Guid.Empty);

            var outboxResult = OutboxMessage.Create<TaskItem, TaskEventType>(task,
                                                                             TaskEventType.Updated,
                                                                             payload,
                                                                             utcNow);

            if (outboxResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Task,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = task.Version,
                    Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            _taskRepository.Update(task);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

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
            var note = await _noteRepository.GetByIdUntrackedAsync(resolution.EntityId, cancellationToken);

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

            // Verify ownership (security boundary)
            if (note.UserId != userId)
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

            if (outboxResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Note,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = note.Version,
                    Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            _noteRepository.Update(note);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

            return new SyncConflictResolutionResultItemDto
            {
                EntityType = SyncEntityType.Note,
                EntityId = resolution.EntityId,
                Status = SyncConflictResolutionStatus.Updated,
                NewVersion = note.Version,
                Errors = Array.Empty<string>()
            };
        }

        // ────────────────────────────────────────────────────────────────────────────
        // Block resolution - NEW: Added for block-based sync
        // ────────────────────────────────────────────────────────────────────────────

        private async Task<SyncConflictResolutionResultItemDto> ResolveBlockConflictAsync(Guid userId,
                                                                                          SyncConflictResolutionDto resolution,
                                                                                          DateTime utcNow,
                                                                                          CancellationToken cancellationToken)
        {
            // Load block WITHOUT tracking (untracked pattern)
            var block = await _blockRepository.GetByIdUntrackedAsync(resolution.EntityId, cancellationToken);

            if (block is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.NotFound,
                    NewVersion = null,
                    Errors = Array.Empty<string>()
                };
            }

            // Verify ownership (security boundary)
            if (block.UserId != userId)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.NotFound,
                    NewVersion = null,
                    Errors = Array.Empty<string>()
                };
            }

            if (block.IsDeleted)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.DeletedOnServer,
                    NewVersion = block.Version,
                    Errors = Array.Empty<string>()
                };
            }

            // KeepServer: just acknowledge, no change
            if (resolution.Choice == SyncResolutionChoice.KeepServer)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.KeptServer,
                    NewVersion = block.Version,
                    Errors = Array.Empty<string>()
                };
            }

            // Check for second-level conflict (server changed again)
            if (block.Version != resolution.ExpectedVersion)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.Conflict,
                    NewVersion = block.Version,
                    Errors = new[]
                    {
                        "Server version does not match ExpectedVersion. The entity has changed again on the server."
                    }
                };
            }

            // Validate BlockData is provided
            if (resolution.BlockData is null)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = block.Version,
                    Errors = new[] { "BlockData must be provided for KeepClient/Merge resolutions." }
                };
            }

            // Validate parent still exists (orphaned blocks are meaningless)
            var parentExists = await ValidateParentExistsAsync(
                block.ParentId,
                block.ParentType,
                userId,
                cancellationToken);

            if (!parentExists)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = block.Version,
                    Errors = new[] { "Parent entity no longer exists or was deleted." }
                };
            }

            // Apply the resolution data (ATOMIC - both position and content)
            var data = resolution.BlockData;
            var errors = new List<string>();

            // Update position if provided and changed
            if (!string.IsNullOrEmpty(data.Position) && data.Position != block.Position)
            {
                var positionResult = block.UpdatePosition(data.Position, utcNow);
                if (positionResult.IsFailure)
                {
                    errors.AddRange(positionResult.Errors.Select(e => e.Message));
                }
            }

            // Update text content if this is a text block
            if (Block.IsTextBlockType(block.Type))
            {
                var contentResult = block.UpdateTextContent(data.TextContent, utcNow);
                if (contentResult.IsFailure)
                {
                    errors.AddRange(contentResult.Errors.Select(e => e.Message));
                }
            }

            if (errors.Count > 0)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = block.Version,
                    Errors = errors
                };
            }

            // Create outbox message BEFORE persisting
            var payload = OutboxPayloadBuilder.BuildBlockPayload(block, Guid.Empty);

            var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                block,
                BlockEventType.Updated,
                payload,
                utcNow);

            if (outboxResult.IsFailure)
            {
                return new SyncConflictResolutionResultItemDto
                {
                    EntityType = SyncEntityType.Block,
                    EntityId = resolution.EntityId,
                    Status = SyncConflictResolutionStatus.ValidationFailed,
                    NewVersion = block.Version,
                    Errors = outboxResult.Errors.Select(e => e.Message).ToArray()
                };
            }

            // Persist both entity and outbox (untracked pattern)
            _blockRepository.Update(block);
            await _outboxRepository.AddAsync(outboxResult.Value, cancellationToken);

            return new SyncConflictResolutionResultItemDto
            {
                EntityType = SyncEntityType.Block,
                EntityId = resolution.EntityId,
                Status = SyncConflictResolutionStatus.Updated,
                NewVersion = block.Version,
                Errors = Array.Empty<string>()
            };
        }


        // ────────────────────────────────────────────────────────────────────────────
        // Helper methods
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that the parent entity exists and belongs to the user.
        /// Returns false if parent is not found, doesn't belong to user, or is deleted.
        /// </summary>
        private async Task<bool> ValidateParentExistsAsync(Guid parentId,
                                                           BlockParentType parentType,
                                                           Guid userId,
                                                           CancellationToken cancellationToken)
        {
            // Only Note is supported as parent (Tasks don't have blocks)
            if (parentType != BlockParentType.Note)
            {
                return false;
            }

            var note = await _noteRepository.GetByIdUntrackedAsync(parentId, cancellationToken);
            return note is not null && note.UserId == userId && !note.IsDeleted;
        }
    }
}
