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
    }
}
