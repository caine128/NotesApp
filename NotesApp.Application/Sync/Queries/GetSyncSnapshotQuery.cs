using FluentResults;
using MediatR;
using NotesApp.Application.Sync.Models;
using System;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Bootstrap snapshot for a new or re-bootstrapping device. Returns the current state of all
    /// the user's non-deleted entities plus the BootstrapSequence cursor.
    /// </summary>
    public sealed record GetSyncSnapshotQuery(Guid? DeviceId) : IRequest<Result<SyncSnapshotDto>>;
}
