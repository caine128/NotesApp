using FluentResults;
using MediatR;
using NotesApp.Application.Sync.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.SyncPush
{
    /// <summary>
    /// Command to apply client-side changes (tasks/notes) to the server.
    /// The payload structure mirrors <see cref="SyncPushCommandPayloadDto"/>.
    /// </summary>
    public sealed class SyncPushCommand : IRequest<Result<SyncPushResultDto>>
    {
        public Guid DeviceId { get; init; }
        public DateTime ClientSyncTimestampUtc { get; init; }

        public SyncPushTasksDto Tasks { get; init; } = new();
        public SyncPushNotesDto Notes { get; init; } = new();
        public SyncPushBlocksDto Blocks { get; init; } = new();
        // REFACTORED: added category push collections for task categories feature
        public SyncPushCategoriesDto Categories { get; init; } = new();
        // REFACTORED: added subtask push collections for subtasks feature
        public SyncPushSubtasksDto Subtasks { get; init; } = new();
        // REFACTORED: added attachment push collections for task-attachments feature
        public SyncPushAttachmentsDto Attachments { get; init; } = new();

        // REFACTORED: added recurring-task push collections for recurring-tasks feature
        /// <summary>
        /// Recurring root creates/deletes from the client device.
        /// Processed before RecurringSeries so within-push RootClientId references resolve.
        /// </summary>
        public SyncPushRecurringRootsDto RecurringRoots { get; init; } = new();

        /// <summary>
        /// Recurring series creates/updates/deletes from the client device.
        /// Processed before RecurringExceptions so within-push SeriesClientId references resolve.
        /// </summary>
        public SyncPushRecurringSeriesDto RecurringSeries { get; init; } = new();

        /// <summary>
        /// Recurring series subtask creates/updates/deletes from the client device.
        /// Covers both series template subtasks and exception subtask overrides.
        /// </summary>
        public SyncPushRecurringSeriesSubtasksDto RecurringSeriesSubtasks { get; init; } = new();

        /// <summary>
        /// Recurring exception creates/updates/deletes from the client device.
        /// Processed after RecurringSeries so SeriesId references are available.
        /// </summary>
        public SyncPushRecurringExceptionsDto RecurringExceptions { get; init; } = new();
    }
}
