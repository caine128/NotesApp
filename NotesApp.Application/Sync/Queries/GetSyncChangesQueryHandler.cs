using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Abstractions.Storage;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Configuration;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Handles sync pull requests from client devices.
    /// 
    /// Returns all changes (tasks, notes, blocks, assets) since a given timestamp:
    /// - Initial sync (SinceUtc = null): Returns all non-deleted entities
    /// - Incremental sync: Categorises entities into created/updated/deleted buckets
    ///   based on CreatedAtUtc vs SinceUtc comparison
    /// 
    /// Features:
    /// - Optional device ownership validation
    /// - Pagination via MaxItemsPerEntity (separate limits per entity type)
    /// - HasMore indicators for truncated results
    /// - Pre-signed download URLs for assets (graceful failure if URL generation fails)
    /// 
    /// Note: This is a read-only query - no outbox messages are emitted.
    /// </summary>
    public sealed class GetSyncChangesQueryHandler
        : IRequestHandler<GetSyncChangesQuery, Result<SyncChangesDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IBlockRepository _blockRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IBlobStorageService? _blobStorageService;
        private readonly AssetStorageOptions _assetOptions;
        private readonly ILogger<GetSyncChangesQueryHandler> _logger;

        public GetSyncChangesQueryHandler(ITaskRepository taskRepository,
                                          INoteRepository noteRepository,
                                          IBlockRepository blockRepository,
                                          IAssetRepository assetRepository,
                                          IUserDeviceRepository deviceRepository,
                                          ICurrentUserService currentUserService,
                                          IOptions<AssetStorageOptions> assetOptions,
                                          ILogger<GetSyncChangesQueryHandler> logger,
                                          IBlobStorageService? blobStorageService = null)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _blockRepository = blockRepository;
            _assetRepository = assetRepository;
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
            _assetOptions = assetOptions.Value;
            _blobStorageService = blobStorageService;
            _logger = logger;
        }

        public async Task<Result<SyncChangesDto>> Handle(GetSyncChangesQuery request,
                                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // Optional device ownership / status check
            if (request.DeviceId is Guid deviceId)
            {
                var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

                if (device is null ||
                    device.UserId != userId ||
                    !device.IsActive ||
                    device.IsDeleted)
                {
                    return Result.Fail(new Error("Device.NotFound"));
                }
            }

            _logger.LogInformation("Sync pull requested for user {UserId} since {SinceUtc} with device {DeviceId}",
                                   userId,
                                   request.SinceUtc,
                                   request.DeviceId);

            // Fetch all changed entities
            var tasks = await _taskRepository.GetChangedSinceAsync(userId,
                                                                   request.SinceUtc,
                                                                   cancellationToken);

            var notes = await _noteRepository.GetChangedSinceAsync(userId,
                                                                   request.SinceUtc,
                                                                   cancellationToken);

            var blocks = await _blockRepository.GetChangedSinceAsync(userId,
                                                                     request.SinceUtc,
                                                                     cancellationToken);

            var assets = await _assetRepository.GetChangedSinceAsync(userId,
                                                                     request.SinceUtc,
                                                                     cancellationToken);

            var serverTimestampUtc = DateTime.UtcNow;

            // Categorise entities into buckets
            var tasksBuckets = CategoriseTasks(tasks, request.SinceUtc);
            var notesBuckets = CategoriseNotes(notes, request.SinceUtc);
            var blocksBuckets = CategoriseBlocks(blocks, request.SinceUtc);
            var assetsBuckets = await CategoriseAssetsAsync(assets, request.SinceUtc, cancellationToken);

            // Determine effective max items per entity
            var effectiveMax = request.MaxItemsPerEntity ?? SyncLimits.DefaultPullMaxItemsPerEntity;

            // Materialise lists so we can safely truncate them
            var taskCreated = tasksBuckets.Created.ToList();
            var taskUpdated = tasksBuckets.Updated.ToList();
            var taskDeleted = tasksBuckets.Deleted.ToList();

            var noteCreated = notesBuckets.Created.ToList();
            var noteUpdated = notesBuckets.Updated.ToList();
            var noteDeleted = notesBuckets.Deleted.ToList();

            var blockCreated = blocksBuckets.Created.ToList();
            var blockUpdated = blocksBuckets.Updated.ToList();
            var blockDeleted = blocksBuckets.Deleted.ToList();

            var assetCreated = assetsBuckets.Created.ToList();
            var assetDeleted = assetsBuckets.Deleted.ToList();

            // Calculate totals for pagination indicators
            var totalTaskChanges = taskCreated.Count + taskUpdated.Count + taskDeleted.Count;
            var totalNoteChanges = noteCreated.Count + noteUpdated.Count + noteDeleted.Count;
            var totalBlockChanges = blockCreated.Count + blockUpdated.Count + blockDeleted.Count;

            var hasMoreTasks = totalTaskChanges > effectiveMax;
            var hasMoreNotes = totalNoteChanges > effectiveMax;
            var hasMoreBlocks = totalBlockChanges > effectiveMax;

            static List<T> LimitList<T>(List<T> source, ref int remaining)
            {
                if (remaining <= 0)
                {
                    return new List<T>();
                }

                if (source.Count <= remaining)
                {
                    remaining -= source.Count;
                    return source;
                }

                var result = source.Take(remaining).ToList();
                remaining = 0;
                return result;
            }

            // Apply limit per entity type independently
            var taskRemaining = effectiveMax;
            var limitedTaskCreated = LimitList(taskCreated, ref taskRemaining);
            var limitedTaskUpdated = LimitList(taskUpdated, ref taskRemaining);
            var limitedTaskDeleted = LimitList(taskDeleted, ref taskRemaining);

            var noteRemaining = effectiveMax;
            var limitedNoteCreated = LimitList(noteCreated, ref noteRemaining);
            var limitedNoteUpdated = LimitList(noteUpdated, ref noteRemaining);
            var limitedNoteDeleted = LimitList(noteDeleted, ref noteRemaining);

            var blockRemaining = effectiveMax;
            var limitedBlockCreated = LimitList(blockCreated, ref blockRemaining);
            var limitedBlockUpdated = LimitList(blockUpdated, ref blockRemaining);
            var limitedBlockDeleted = LimitList(blockDeleted, ref blockRemaining);

            // Assets don't have a limit currently (they're typically fewer and essential)
            var limitedAssetCreated = assetCreated;
            var limitedAssetDeleted = assetDeleted;

            var limitedTasksBuckets = new SyncTasksChangesDto
            {
                Created = limitedTaskCreated,
                Updated = limitedTaskUpdated,
                Deleted = limitedTaskDeleted
            };

            var limitedNotesBuckets = new SyncNotesChangesDto
            {
                Created = limitedNoteCreated,
                Updated = limitedNoteUpdated,
                Deleted = limitedNoteDeleted
            };

            var limitedBlocksBuckets = new SyncBlocksChangesDto
            {
                Created = limitedBlockCreated,
                Updated = limitedBlockUpdated,
                Deleted = limitedBlockDeleted
            };

            var limitedAssetsBuckets = new SyncAssetsChangesDto
            {
                Created = limitedAssetCreated,
                Deleted = limitedAssetDeleted
            };

            var dto = new SyncChangesDto
            {
                ServerTimestampUtc = serverTimestampUtc,
                Tasks = limitedTasksBuckets,
                Notes = limitedNotesBuckets,
                Blocks = limitedBlocksBuckets,
                Assets = limitedAssetsBuckets,
                HasMoreTasks = hasMoreTasks,
                HasMoreNotes = hasMoreNotes,
                HasMoreBlocks = hasMoreBlocks
            };

            return Result.Ok(dto);
        }

        private static SyncTasksChangesDto CategoriseTasks(IReadOnlyList<TaskItem> tasks,
                                                           DateTime? sinceUtc)
        {
            // Initial sync (since == null): everything is "created" (non-deleted only,
            // because the repository already filters out deleted items in that branch).
            if (sinceUtc is null)
            {
                var created = tasks
                    .Where(t => !t.IsDeleted)
                    .OrderBy(t => t.UpdatedAtUtc)
                    .Select(t => t.ToSyncDto())
                    .ToList();

                return new SyncTasksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<TaskSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<TaskSyncItemDto>();
            var updatedList = new List<TaskSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var task in tasks.OrderBy(t => t.UpdatedAtUtc))
            {
                if (task.IsDeleted)
                {
                    // Treat any deleted item in the window as "deleted" regardless of CreatedAtUtc,
                    // using UpdatedAtUtc as the deletion timestamp.
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = task.Id,
                        DeletedAtUtc = task.UpdatedAtUtc
                    });

                    continue;
                }

                if (task.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(task.ToSyncDto());
                }
                else
                {
                    updatedList.Add(task.ToSyncDto());
                }
            }

            return new SyncTasksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncNotesChangesDto CategoriseNotes(IReadOnlyList<Note> notes,
                                                           DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = notes
                    .Where(n => !n.IsDeleted)
                    .OrderBy(n => n.UpdatedAtUtc)
                    .Select(n => n.ToSyncDto())
                    .ToList();

                return new SyncNotesChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<NoteSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<NoteSyncItemDto>();
            var updatedList = new List<NoteSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var note in notes.OrderBy(n => n.UpdatedAtUtc))
            {
                if (note.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = note.Id,
                        DeletedAtUtc = note.UpdatedAtUtc
                    });

                    continue;
                }

                if (note.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(note.ToSyncDto());
                }
                else
                {
                    updatedList.Add(note.ToSyncDto());
                }
            }

            return new SyncNotesChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        private static SyncBlocksChangesDto CategoriseBlocks(IReadOnlyList<Block> blocks,
                                                             DateTime? sinceUtc)
        {
            if (sinceUtc is null)
            {
                var created = blocks
                    .Where(b => !b.IsDeleted)
                    .OrderBy(b => b.UpdatedAtUtc)
                    .Select(b => b.ToSyncDto())
                    .ToList();

                return new SyncBlocksChangesDto
                {
                    Created = created,
                    Updated = Array.Empty<BlockSyncItemDto>(),
                    Deleted = Array.Empty<DeletedSyncItemDto>()
                };
            }

            var createdList = new List<BlockSyncItemDto>();
            var updatedList = new List<BlockSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var block in blocks.OrderBy(b => b.UpdatedAtUtc))
            {
                if (block.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = block.Id,
                        DeletedAtUtc = block.UpdatedAtUtc
                    });

                    continue;
                }

                if (block.CreatedAtUtc > sinceUtc.Value)
                {
                    createdList.Add(block.ToSyncDto());
                }
                else
                {
                    updatedList.Add(block.ToSyncDto());
                }
            }

            return new SyncBlocksChangesDto
            {
                Created = createdList,
                Updated = updatedList,
                Deleted = deletedList
            };
        }

        /// <summary>
        /// Categorises assets into created/deleted buckets.
        /// Assets are immutable, so there's no "updated" bucket.
        /// Generates pre-signed download URLs for created assets.
        /// </summary>
        private async Task<SyncAssetsChangesDto> CategoriseAssetsAsync(IReadOnlyList<Asset> assets,
                                                                       DateTime? sinceUtc,
                                                                       CancellationToken cancellationToken)
        {
            var createdList = new List<AssetSyncItemDto>();
            var deletedList = new List<DeletedSyncItemDto>();

            foreach (var asset in assets.OrderBy(a => a.UpdatedAtUtc))
            {
                if (asset.IsDeleted)
                {
                    deletedList.Add(new DeletedSyncItemDto
                    {
                        Id = asset.Id,
                        DeletedAtUtc = asset.UpdatedAtUtc
                    });

                    continue;
                }

                // Generate pre-signed download URL for the asset
                string? downloadUrl = null;
                if (_blobStorageService is not null)
                {
                    var urlResult = await _blobStorageService.GenerateDownloadUrlAsync(_assetOptions.ContainerName,
                                                                                       asset.BlobPath,
                                                                                       _assetOptions.DownloadUrlValidity,
                                                                                       cancellationToken);

                    if (urlResult.IsSuccess)
                    {
                        downloadUrl = urlResult.Value;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to generate download URL for asset {AssetId}: {Errors}",
                            asset.Id,
                            string.Join(", ", urlResult.Errors.Select(e => e.Message)));
                        // Continue without download URL - client can request it separately
                    }
                }

                createdList.Add(asset.ToSyncDto(downloadUrl));
            }

            return new SyncAssetsChangesDto
            {
                Created = createdList,
                Deleted = deletedList
            };
        }
    }
}
