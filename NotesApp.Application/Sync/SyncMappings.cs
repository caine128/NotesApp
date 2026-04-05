using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync
{
    public static class SyncMappings
    {
        public static TaskSyncItemDto ToSyncDto(this TaskItem task)
        {
            return new TaskSyncItemDto
            {
                Id = task.Id,
                Date = task.Date,
                Title = task.Title,
                IsCompleted = task.IsCompleted,
                Description = task.Description,
                StartTime = task.StartTime,
                EndTime = task.EndTime,
                Location = task.Location,
                TravelTime = task.TravelTime,
                ReminderAtUtc = task.ReminderAtUtc,
                CategoryId = task.CategoryId, // REFACTORED: added CategoryId mapping
                Priority = task.Priority, // REFACTORED: added Priority mapping
                Version = task.Version,
                CreatedAtUtc = task.CreatedAtUtc,
                UpdatedAtUtc = task.UpdatedAtUtc
            };
        }

        public static NoteSyncItemDto ToSyncDto(this Note note)
        {
            return new NoteSyncItemDto
            {
                Id = note.Id,
                Date = note.Date,
                Title = note.Title,
                Summary = note.Summary,
                Tags = note.Tags,
                Version = note.Version,
                CreatedAtUtc = note.CreatedAtUtc,
                UpdatedAtUtc = note.UpdatedAtUtc
            };
        }

        public static BlockSyncItemDto ToSyncDto(this Block block)
        {
            return new BlockSyncItemDto
            {
                Id = block.Id,
                ParentId = block.ParentId,
                ParentType = block.ParentType,
                Type = block.Type,
                Position = block.Position,
                TextContent = block.TextContent,
                AssetId = block.AssetId,
                AssetClientId = block.AssetClientId,
                AssetFileName = block.AssetFileName,
                AssetContentType = block.AssetContentType,
                AssetSizeBytes = block.AssetSizeBytes,
                UploadStatus = block.UploadStatus,
                Version = block.Version,
                CreatedAtUtc = block.CreatedAtUtc,
                UpdatedAtUtc = block.UpdatedAtUtc
            };
        }

        public static AssetSyncItemDto ToSyncDto(this Asset asset)
        {
            return new AssetSyncItemDto
            {
                Id = asset.Id,
                BlockId = asset.BlockId,
                FileName = asset.FileName,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                CreatedAtUtc = asset.CreatedAtUtc,
                UpdatedAtUtc = asset.UpdatedAtUtc
            };
        }

        // REFACTORED: added TaskCategory sync mapping for task categories feature
        /// <summary>
        /// Maps a <see cref="TaskCategory"/> to its sync pull representation.
        /// </summary>
        public static CategorySyncItemDto ToSyncDto(this TaskCategory category)
        {
            return new CategorySyncItemDto
            {
                Id = category.Id,
                Name = category.Name,
                Version = category.Version,
                CreatedAtUtc = category.CreatedAtUtc,
                UpdatedAtUtc = category.UpdatedAtUtc
            };
        }

        public static IReadOnlyList<TaskSyncItemDto> ToSyncTaskDtos(this IEnumerable<TaskItem> tasks)
            => tasks.Select(t => t.ToSyncDto()).ToList();

        public static IReadOnlyList<NoteSyncItemDto> ToSyncNoteDtos(this IEnumerable<Note> notes)
            => notes.Select(n => n.ToSyncDto()).ToList();

        public static IReadOnlyList<BlockSyncItemDto> ToSyncBlockDtos(this IEnumerable<Block> blocks)
            => blocks.Select(b => b.ToSyncDto()).ToList();

        // REFACTORED: added category list mapping for sync pull
        public static IReadOnlyList<CategorySyncItemDto> ToSyncCategoryDtos(this IEnumerable<TaskCategory> categories)
            => categories.Select(c => c.ToSyncDto()).ToList();

        // REFACTORED: added Subtask sync mapping for subtasks feature
        /// <summary>
        /// Maps a <see cref="Subtask"/> to its sync pull/conflict representation.
        /// </summary>
        public static SubtaskSyncItemDto ToSyncDto(this Subtask subtask)
        {
            return new SubtaskSyncItemDto
            {
                Id = subtask.Id,
                TaskId = subtask.TaskId,
                Text = subtask.Text,
                IsCompleted = subtask.IsCompleted,
                Position = subtask.Position,
                Version = subtask.Version,
                CreatedAtUtc = subtask.CreatedAtUtc,
                UpdatedAtUtc = subtask.UpdatedAtUtc
            };
        }

        // REFACTORED: added subtask list mapping for sync pull
        public static IReadOnlyList<SubtaskSyncItemDto> ToSyncSubtaskDtos(this IEnumerable<Subtask> subtasks)
            => subtasks.Select(s => s.ToSyncDto()).ToList();
    }
}
