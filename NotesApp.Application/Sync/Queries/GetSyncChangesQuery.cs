using FluentResults;
using MediatR;
using NotesApp.Application.Sync.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Query for pulling changes since a given timestamp for the current user.
    /// </summary>
    public sealed record GetSyncChangesQuery(DateTime? SinceUtc, Guid? DeviceId)
        : IRequest<Result<SyncChangesDto>>;
}
