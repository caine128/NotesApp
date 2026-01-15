using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Blocks.Models;
using NotesApp.Application.Common;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace NotesApp.Application.Blocks.Commands.UpdateBlock
{
    /// <summary>
    /// Handles the UpdateBlockCommand:
    /// - Loads the block WITHOUT tracking to prevent auto-persistence on failure.
    /// - Ensures the block exists and belongs to the current user.
    /// - Applies domain logic (position, text content updates).
    /// - Creates outbox message BEFORE persisting.
    /// - Persists changes only after all validations succeed.
    /// </summary>
    public sealed class UpdateBlockCommandHandler
        : IRequestHandler<UpdateBlockCommand, Result<BlockDetailDto>>
    {
        private readonly IBlockRepository _blockRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<UpdateBlockCommandHandler> _logger;

        public UpdateBlockCommandHandler(
            IBlockRepository blockRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<UpdateBlockCommandHandler> logger)
        {
            _blockRepository = blockRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<BlockDetailDto>> Handle(UpdateBlockCommand command,
                                                         CancellationToken cancellationToken)
        {
            // 1) Resolve current user
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 2) Load the block WITHOUT tracking
            //    This ensures modifications won't auto-persist if we return early due to failures
            var block = await _blockRepository.GetByIdUntrackedAsync(command.BlockId, cancellationToken);

            if (block is null || block.UserId != userId)
            {
                _logger.LogWarning(
                    "UpdateBlock failed: Block {BlockId} not found for user {UserId}",
                    command.BlockId,
                    userId);

                return Result.Fail<BlockDetailDto>(
                    new Error("Block not found.")
                        .WithMetadata("ErrorCode", "Blocks.NotFound"));
            }

            if (block.IsDeleted)
            {
                return Result.Fail<BlockDetailDto>(
                    new Error("Cannot update a deleted block.")
                        .WithMetadata("ErrorCode", "Blocks.Deleted"));
            }

            // 3) Track if any changes were made
            var hasChanges = false;

            // 4) Update position if provided (entity is NOT tracked, so modifications are in-memory only)
            if (!string.IsNullOrEmpty(command.Position))
            {
                var positionResult = block.UpdatePosition(command.Position, utcNow);
                if (positionResult.IsFailure)
                {
                    // Entity modified but NOT tracked - won't persist
                    return positionResult.ToResult(() => block.ToDetailDto());
                }
                hasChanges = true;
            }

            // 5) Update text content if provided (only for text blocks)
            if (command.TextContent is not null)
            {
                var textResult = block.UpdateTextContent(command.TextContent, utcNow);
                if (textResult.IsFailure)
                {
                    // Entity modified but NOT tracked - won't persist
                    return textResult.ToResult(() => block.ToDetailDto());
                }
                hasChanges = true;
            }

            // 6) If no changes, return current state without persisting
            if (!hasChanges)
            {
                return Result.Ok(block.ToDetailDto());
            }

            // 7) Create outbox message BEFORE persisting
            var payload = JsonSerializer.Serialize(new
            {
                BlockId = block.Id,
                block.UserId,
                block.ParentId,
                ParentType = block.ParentType.ToString(),
                Type = block.Type.ToString(),
                block.Position,
                block.TextContent,
                block.Version,
                Event = BlockEventType.Updated.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Block, BlockEventType>(
                aggregate: block,
                eventType: BlockEventType.Updated,
                payload: payload,
                utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                // Entity modified but NOT tracked - won't persist
                return outboxResult.ToResult<OutboxMessage, BlockDetailDto>(_ => block.ToDetailDto());
            }

            // 8) SUCCESS: Now explicitly attach and persist
            //    Update() attaches the untracked entity and marks it as Modified
            _blockRepository.Update(block);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Block {BlockId} updated for user {UserId}",
                block.Id,
                userId);

            return Result.Ok(block.ToDetailDto());
        }
    }
}
