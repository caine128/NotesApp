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
                MeetingLink = task.MeetingLink, // REFACTORED: added MeetingLink mapping
                // REFACTORED: added recurring-task fields for recurring-tasks feature
                RecurringSeriesId = task.RecurringSeriesId,
                CanonicalOccurrenceDate = task.CanonicalOccurrenceDate,
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

        // REFACTORED: added Attachment sync mapping for task-attachments feature
        /// <summary>
        /// Maps an <see cref="Attachment"/> to its sync pull representation.
        /// Download URLs are not included; use GET /api/attachments/{id}/download-url on demand.
        /// </summary>
        public static AttachmentSyncItemDto ToSyncDto(this Attachment attachment)
        {
            return new AttachmentSyncItemDto
            {
                Id = attachment.Id,
                TaskId = attachment.TaskId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                DisplayOrder = attachment.DisplayOrder,
                CreatedAtUtc = attachment.CreatedAtUtc,
                UpdatedAtUtc = attachment.UpdatedAtUtc
            };
        }

        // REFACTORED: added recurring-task sync mappings for recurring-tasks feature

        /// <summary>Maps a <see cref="RecurringTaskRoot"/> to its sync pull representation.</summary>
        public static RecurringRootSyncItemDto ToSyncDto(this RecurringTaskRoot root)
        {
            return new RecurringRootSyncItemDto
            {
                Id = root.Id,
                UserId = root.UserId,
                Version = root.Version,
                CreatedAtUtc = root.CreatedAtUtc,
                UpdatedAtUtc = root.UpdatedAtUtc
            };
        }

        /// <summary>
        /// Maps a <see cref="RecurringTaskSeries"/> to its sync pull representation.
        /// RRuleString is sent verbatim for client-side iCal parsing.
        /// </summary>
        public static RecurringSeriesSyncItemDto ToSyncDto(this RecurringTaskSeries series)
        {
            return new RecurringSeriesSyncItemDto
            {
                Id = series.Id,
                UserId = series.UserId,
                RootId = series.RootId,
                RRuleString = series.RRuleString,
                StartsOnDate = series.StartsOnDate,
                EndsBeforeDate = series.EndsBeforeDate,
                Title = series.Title,
                Description = series.Description,
                StartTime = series.StartTime,
                EndTime = series.EndTime,
                Location = series.Location,
                TravelTime = series.TravelTime,
                CategoryId = series.CategoryId,
                Priority = series.Priority,
                MeetingLink = series.MeetingLink,
                ReminderOffsetMinutes = series.ReminderOffsetMinutes,
                MaterializedUpToDate = series.MaterializedUpToDate,
                Version = series.Version,
                CreatedAtUtc = series.CreatedAtUtc,
                UpdatedAtUtc = series.UpdatedAtUtc
            };
        }

        /// <summary>
        /// Maps a <see cref="RecurringTaskSubtask"/> to its sync pull representation.
        /// Covers both series template subtasks (SeriesId set) and exception overrides (ExceptionId set).
        /// </summary>
        public static RecurringSubtaskSyncItemDto ToSyncDto(this RecurringTaskSubtask subtask)
        {
            return new RecurringSubtaskSyncItemDto
            {
                Id = subtask.Id,
                UserId = subtask.UserId,
                SeriesId = subtask.SeriesId,
                ExceptionId = subtask.ExceptionId,
                Text = subtask.Text,
                IsCompleted = subtask.IsCompleted,
                Position = subtask.Position,
                Version = subtask.Version,
                CreatedAtUtc = subtask.CreatedAtUtc,
                UpdatedAtUtc = subtask.UpdatedAtUtc
            };
        }

        /// <summary>
        /// Maps a <see cref="RecurringTaskException"/> to its sync pull representation.
        /// Subtasks are pre-loaded by the caller via GetByExceptionIdsAsync and passed in.
        /// </summary>
        public static RecurringExceptionSyncItemDto ToSyncDto(
            this RecurringTaskException exception,
            IReadOnlyList<RecurringSubtaskSyncItemDto> subtasks)
        {
            return new RecurringExceptionSyncItemDto
            {
                Id = exception.Id,
                UserId = exception.UserId,
                SeriesId = exception.SeriesId,
                OccurrenceDate = exception.OccurrenceDate,
                IsDeletion = exception.IsDeletion,
                OverrideTitle = exception.OverrideTitle,
                OverrideDescription = exception.OverrideDescription,
                OverrideDate = exception.OverrideDate,
                OverrideStartTime = exception.OverrideStartTime,
                OverrideEndTime = exception.OverrideEndTime,
                OverrideLocation = exception.OverrideLocation,
                OverrideTravelTime = exception.OverrideTravelTime,
                OverrideCategoryId = exception.OverrideCategoryId,
                OverridePriority = exception.OverridePriority,
                OverrideMeetingLink = exception.OverrideMeetingLink,
                OverrideReminderAtUtc = exception.OverrideReminderAtUtc,
                IsCompleted = exception.IsCompleted,
                MaterializedTaskItemId = exception.MaterializedTaskItemId,
                Subtasks = subtasks,
                Version = exception.Version,
                CreatedAtUtc = exception.CreatedAtUtc,
                UpdatedAtUtc = exception.UpdatedAtUtc
            };
        }
    }
}
