using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Notes.Commands.DeleteNote
{
    /// <summary>
    /// Handles the DeleteNoteCommand:
    /// - Resolves the current internal user id from the token.
    /// - Loads the note WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the note belongs to the current user.
    /// - Soft-deletes the note through the domain method.
    /// - CASCADE: Soft-deletes all blocks belonging to this note.
    /// - Creates outbox messages for note and all affected blocks.
    /// - Persists changes only after all operations succeed.
    /// 
    /// Returns:
    /// - Result.Ok()                 -> HTTP 204 No Content
    /// - Result.Fail (Notes.NotFound)-> HTTP 404 Not Found
    /// - Other failures              -> HTTP 400 / 500 via global mapping.
    /// </summary>
    public sealed class DeleteNoteCommandHandler
         : IRequestHandler<DeleteNoteCommand, Result>
    {
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;  // ADDED: For cascade deletion
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteNoteCommandHandler> _logger;

        public DeleteNoteCommandHandler(
            INoteRepository noteRepository,
            IBlockRepository blockRepository,  // ADDED
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteNoteCommandHandler> logger)
        {
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;  // ADDED
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteNoteCommand request, CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // 2) Load the note WITHOUT tracking
            //    This ensures the soft-delete modification won't auto-persist if outbox creation fails
            var note = await _noteRepository.GetByIdUntrackedAsync(request.NoteId, cancellationToken);

            if (note is null || note.UserId != userId)
            {
                _logger.LogWarning("DeleteNote failed: note {NoteId} not found for user {UserId}.",
                                   request.NoteId,
                                   userId);

                return Result.Fail(
                    new Error("Note not found.")
                        .WithMetadata("ErrorCode", "Notes.NotFound"));
            }

            var utcNow = _clock.UtcNow;

            // 3) Domain soft delete (entity is NOT tracked, so modifications are in-memory only)
            var deleteResult = note.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return deleteResult.ToResult();
            }

            // 4) Create outbox message BEFORE persisting
            var notePayload = JsonSerializer.Serialize(new
            {
                NoteId = note.Id,
                note.UserId,
                note.Date,
                note.Title,
                Event = NoteEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var noteOutboxResult = OutboxMessage.Create<Note, NoteEventType>(aggregate: note,
                                                                         eventType: NoteEventType.Deleted,
                                                                         payload: notePayload,
                                                                         utcNow: utcNow);

            if (noteOutboxResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return noteOutboxResult.ToResult();
            }

            // 5) CASCADE: Soft-delete all blocks belonging to this note
            //    Use UNTRACKED retrieval to ensure atomicity - blocks won't auto-persist
            //    if any block fails to create an outbox message
            var blocks = await _blockRepository.GetForParentUntrackedAsync(note.Id,
                                                                           BlockParentType.Note,
                                                                           cancellationToken);

            var blockOutboxMessages = new List<OutboxMessage>();
            var blocksToUpdate = new List<Block>();

            foreach (var block in blocks)
            {
                // Soft-delete the block
                var blockDeleteResult = block.SoftDelete(utcNow);
                if (blockDeleteResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to soft-delete block {BlockId} during note cascade deletion: {Errors}",
                        block.Id,
                        string.Join(", ", blockDeleteResult.Errors.Select(e => e.Message)));

                    // Continue with other blocks - don't fail entire operation
                    continue;
                }

                // Create outbox message for block
                var blockPayload = OutboxPayloadBuilder.BuildBlockPayload(block, Guid.Empty);
                var blockOutboxResult = OutboxMessage.Create<Block, BlockEventType>(
                    aggregate: block,
                    eventType: BlockEventType.Deleted,
                    payload: blockPayload,
                    utcNow: utcNow);

                if (blockOutboxResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Failed to create outbox message for block {BlockId}: {Errors}",
                        block.Id,
                        string.Join(", ", blockOutboxResult.Errors.Select(e => e.Message)));

                    // Block was modified but outbox failed - don't persist this block
                    // Since it's untracked, it won't auto-persist
                    continue;
                }

                // Both operations succeeded - mark for persistence
                blocksToUpdate.Add(block);
                blockOutboxMessages.Add(blockOutboxResult.Value);
            }

            // 6) SUCCESS: Now explicitly attach and persist all entities
            //    Update() attaches the untracked entity and marks it as Modified
            _noteRepository.Update(note);
            await _outboxRepository.AddAsync(noteOutboxResult.Value!, cancellationToken);

            // Persist block changes - only blocks where both soft-delete AND outbox succeeded
            foreach (var block in blocksToUpdate)
            {
                _blockRepository.Update(block);
            }

            foreach (var outboxMessage in blockOutboxMessages)
            {
                await _outboxRepository.AddAsync(outboxMessage, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Note {NoteId} soft-deleted for user {UserId}.",
                note.Id,
                userId);

            return Result.Ok();
        }
    }
}
