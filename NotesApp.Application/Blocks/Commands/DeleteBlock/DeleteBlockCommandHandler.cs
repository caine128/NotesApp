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

namespace NotesApp.Application.Blocks.Commands.DeleteBlock
{
    /// <summary>
    /// Handles the DeleteBlockCommand:
    /// - Resolves the current internal user id from the token.
    /// - Loads the block WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the block belongs to the current user.
    /// - Soft-deletes the block through the domain method.
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    /// 
    /// Returns:
    /// - Result.Ok()                  -> HTTP 204 No Content
    /// - Result.Fail (Blocks.NotFound)-> HTTP 404 Not Found
    /// - Other failures               -> HTTP 400 / 500 via global mapping
    /// </summary>
    public sealed class DeleteBlockCommandHandler
        : IRequestHandler<DeleteBlockCommand, Result>
    {
        private readonly IBlockRepository _blockRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<DeleteBlockCommandHandler> _logger;

        public DeleteBlockCommandHandler(
            IBlockRepository blockRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<DeleteBlockCommandHandler> logger)
        {
            _blockRepository = blockRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result> Handle(DeleteBlockCommand command,
                                         CancellationToken cancellationToken)
        {
            // 1) Resolve the current internal user id
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 2) Load the block WITHOUT tracking
            //    This ensures the soft-delete modification won't auto-persist if outbox creation fails
            var block = await _blockRepository.GetByIdUntrackedAsync(command.BlockId, cancellationToken);

            if (block is null || block.UserId != userId)
            {
                _logger.LogWarning(
                    "DeleteBlock failed: Block {BlockId} not found for user {UserId}",
                    command.BlockId,
                    userId);

                return Result.Fail(
                    new Error("Block not found.")
                        .WithMetadata("ErrorCode", "Blocks.NotFound"));
            }

            // 3) Domain soft delete (entity is NOT tracked, so modifications are in-memory only)
            var deleteResult = block.SoftDelete(utcNow);

            if (deleteResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return deleteResult.ToResult();
            }

            // 4) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                BlockId = block.Id,
                block.UserId,
                block.ParentId,
                ParentType = block.ParentType.ToString(),
                Type = block.Type.ToString(),
                Event = BlockEventType.Deleted.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                aggregate: block,
                eventType: BlockEventType.Deleted,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult();
            }

            // 5) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _blockRepository.Update(block);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Block {BlockId} soft-deleted for user {UserId}",
                block.Id,
                userId);

            return Result.Ok();
        }
    }
}
