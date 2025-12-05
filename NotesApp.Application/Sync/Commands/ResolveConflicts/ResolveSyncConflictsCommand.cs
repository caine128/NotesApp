using FluentResults;
using MediatR;
using NotesApp.Application.Sync.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Commands.ResolveConflicts
{
    /// <summary>
    /// Command to resolve previously reported sync conflicts for tasks/notes.
    /// </summary>
    public sealed class ResolveSyncConflictsCommand : IRequest<Result<ResolveSyncConflictsResultDto>>
    {
        public ResolveSyncConflictsRequestDto Request { get; init; } = new();
    }
}
