using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Sync.Queries
{
    /// <summary>
    /// Handles sync pull requests for tasks and notes.
    /// 
    /// Responsibilities:
    /// - Determine current user from ICurrentUserService.
    /// - Load changed tasks and notes via repositories.
    /// - Categorise them into created / updated / deleted buckets.
    /// - Stamp the response with a server-side timestamp in UTC.
    /// </summary>
    public sealed class GetSyncChangesQueryHandler
        : IRequestHandler<GetSyncChangesQuery, Result<SyncChangesDto>>
    {
        private readonly ITaskRepository _taskRepository;
        private readonly INoteRepository _noteRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetSyncChangesQueryHandler> _logger;

        public GetSyncChangesQueryHandler(ITaskRepository taskRepository,
                                          INoteRepository noteRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetSyncChangesQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<SyncChangesDto>> Handle(GetSyncChangesQuery request,
                                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            _logger.LogInformation(
                "Sync pull requested for user {UserId} since {SinceUtc} with device {DeviceId}",
                userId,
                request.SinceUtc,
                request.DeviceId);

            var tasks = await _taskRepository.GetChangedSinceAsync(
                userId,
                request.SinceUtc,
                cancellationToken);

            var notes = await _noteRepository.GetChangedSinceAsync(
                userId,
                request.SinceUtc,
                cancellationToken);

            var serverTimestampUtc = DateTime.UtcNow;

            var tasksBuckets = CategoriseTasks(tasks, request.SinceUtc);
            var notesBuckets = CategoriseNotes(notes, request.SinceUtc);

            var dto = new SyncChangesDto
            {
                ServerTimestampUtc = serverTimestampUtc,
                Tasks = tasksBuckets,
                Notes = notesBuckets
            };

            return Result.Ok(dto);
        }

        private static SyncTasksChangesDto CategoriseTasks(
            IReadOnlyList<TaskItem> tasks,
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

        private static SyncNotesChangesDto CategoriseNotes(
            IReadOnlyList<Note> notes,
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
    }
}
