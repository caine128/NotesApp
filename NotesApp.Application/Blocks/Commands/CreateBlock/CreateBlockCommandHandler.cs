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

namespace NotesApp.Application.Blocks.Commands.CreateBlock
{
    /// <summary>
    /// Handles the CreateBlockCommand:
    /// - Resolves the current user from the token
    /// - Validates that the parent (Note/Task) exists and belongs to the user
    /// - Creates the block via domain factory methods
    /// - Emits an outbox message for the Created event
    /// - Persists changes via UnitOfWork
    /// </summary>
    public sealed class CreateBlockCommandHandler
        : IRequestHandler<CreateBlockCommand, Result<BlockDetailDto>>
    {
        private readonly IBlockRepository _blockRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ITaskRepository _taskRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUserService;
        private readonly ISystemClock _clock;
        private readonly ILogger<CreateBlockCommandHandler> _logger;

        public CreateBlockCommandHandler(
            IBlockRepository blockRepository,
            INoteRepository noteRepository,
            ITaskRepository taskRepository,
            IOutboxRepository outboxRepository,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUserService,
            ISystemClock clock,
            ILogger<CreateBlockCommandHandler> logger)
        {
            _blockRepository = blockRepository;
            _noteRepository = noteRepository;
            _taskRepository = taskRepository;
            _outboxRepository = outboxRepository;
            _unitOfWork = unitOfWork;
            _currentUserService = currentUserService;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<BlockDetailDto>> Handle(CreateBlockCommand command,
                                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);
            var utcNow = _clock.UtcNow;

            // 1) Validate parent exists and belongs to user
            var parentValidation = await ValidateParentAsync(command.ParentId,
                                                             command.ParentType,
                                                             userId,
                                                             cancellationToken);

            if (parentValidation.IsFailed)
            {
                return parentValidation;
            }

            // 2) Create block via domain factory
            DomainResult<Block> createResult;

            if (Block.IsTextBlockType(command.Type))
            {
                createResult = Block.CreateTextBlock(userId: userId,
                                                     parentId: command.ParentId,
                                                     parentType: command.ParentType,
                                                     type: command.Type,
                                                     position: command.Position,
                                                     textContent: command.TextContent,
                                                     utcNow: utcNow);
            }
            else if (Block.IsAssetBlockType(command.Type))
            {
                createResult = Block.CreateAssetBlock(userId: userId,
                                                      parentId: command.ParentId,
                                                      parentType: command.ParentType,
                                                      type: command.Type,
                                                      position: command.Position,
                                                      assetClientId: command.AssetClientId!,
                                                      assetFileName: command.AssetFileName!,
                                                      assetContentType: command.AssetContentType,
                                                      assetSizeBytes: command.AssetSizeBytes!.Value,
                                                      utcNow: utcNow);
            }
            else
            {
                return Result.Fail<BlockDetailDto>(
                    new Error($"Unknown block type: {command.Type}")
                        .WithMetadata("ErrorCode", "Blocks.InvalidType"));
            }

            if (createResult.IsFailure)
            {
                return createResult.ToResult<Block, BlockDetailDto>(b => b.ToDetailDto());
            }

            var block = createResult.Value!;

            // 3) Create outbox message
            var payload = JsonSerializer.Serialize(new
            {
                BlockId = block.Id,
                block.UserId,
                block.ParentId,
                ParentType = block.ParentType.ToString(),
                Type = block.Type.ToString(),
                block.Position,
                block.TextContent,
                block.AssetClientId,
                block.AssetFileName,
                block.AssetContentType,
                block.AssetSizeBytes,
                Event = BlockEventType.Created.ToString(),
                OccurredAtUtc = utcNow
            });

            var outboxResult = OutboxMessage.Create<Block, BlockEventType>(aggregate: block,
                                                                           eventType: BlockEventType.Created,
                                                                           payload: payload,
                                                                           utcNow: utcNow);

            if (outboxResult.IsFailure)
            {
                return outboxResult.ToResult<OutboxMessage, BlockDetailDto>(_ => block.ToDetailDto());
            }

            // 4) Persist
            await _blockRepository.AddAsync(block, cancellationToken);
            await _outboxRepository.AddAsync(outboxResult.Value!, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Block {BlockId} created for parent {ParentType}:{ParentId} by user {UserId}",
                block.Id,
                command.ParentType,
                command.ParentId,
                userId);

            return Result.Ok(block.ToDetailDto());
        }

        /// <summary>
        /// Validates that the parent entity exists and belongs to the current user.
        /// </summary>
        private async Task<Result<BlockDetailDto>> ValidateParentAsync(Guid parentId,
                                                                       BlockParentType parentType,
                                                                       Guid userId,
                                                                       CancellationToken cancellationToken)
        {
            switch (parentType)
            {
                case BlockParentType.Note:
                    var note = await _noteRepository.GetByIdAsync(parentId, cancellationToken);
                    if (note is null || note.UserId != userId || note.IsDeleted)
                    {
                        _logger.LogWarning(
                            "CreateBlock failed: Note {NoteId} not found for user {UserId}",
                            parentId,
                            userId);

                        return Result.Fail<BlockDetailDto>(
                            new Error("Parent note not found.")
                                .WithMetadata("ErrorCode", "Blocks.ParentNotFound"));
                    }
                    break;

                default:
                    return Result.Fail<BlockDetailDto>(
                        new Error($"Unknown parent type: {parentType}")
                            .WithMetadata("ErrorCode", "Blocks.InvalidParentType"));
            }

            return Result.Ok();
        }
    }
}
