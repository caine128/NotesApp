using FluentResults;
using MediatR;
using NotesApp.Application.Sync.Models;
using System;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Sequence-based sync pull. Returns ordered SyncChange rows after <paramref name="AfterSequence"/>.
    /// Replaces the legacy timestamp-based <c>GetSyncChangesQuery</c>.
    /// </summary>
    /// <param name="AfterSequence">Resume cursor; 0 for "from the beginning of available history".</param>
    /// <param name="DeviceId">Optional device id; when provided, validates ownership and advances
    /// the device's LastAckedSyncSequence on a successful pull.</param>
    /// <param name="Limit">Optional page size; falls back to <c>SyncPullLimits.DefaultPullLimit</c> when null.</param>
    public sealed record GetSyncPullQuery(long AfterSequence, Guid? DeviceId, int? Limit)
        : IRequest<Result<SyncPullDto>>;
}
