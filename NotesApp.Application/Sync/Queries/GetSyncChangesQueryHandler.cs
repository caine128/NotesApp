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
        private readonly IUserDeviceRepository _deviceRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<GetSyncChangesQueryHandler> _logger;

        public GetSyncChangesQueryHandler(ITaskRepository taskRepository,
                                          INoteRepository noteRepository,
                                          IUserDeviceRepository deviceRepository,
                                          ICurrentUserService currentUserService,
                                          ILogger<GetSyncChangesQueryHandler> logger)
        {
            _taskRepository = taskRepository;
            _noteRepository = noteRepository;
            _deviceRepository = deviceRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Result<SyncChangesDto>> Handle(GetSyncChangesQuery request,
                                                         CancellationToken cancellationToken)
        {
            var userId = await _currentUserService.GetUserIdAsync(cancellationToken);

            // NEW: optional device ownership / status check
            if (request.DeviceId is Guid deviceId)
            {
                var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);

                if (device is null ||
                    device.UserId != userId ||
                    !device.IsActive ||    // avoid using inactive devices
                    device.IsDeleted)      // if global filters ever let it through
                {
                    return Result.Fail(new Error("Device.NotFound"));
                }
            }

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

            // Determine effective max items per entity (tasks/notes)
            var effectiveMax = request.MaxItemsPerEntity ?? SyncLimits.DefaultPullMaxItemsPerEntity; ;

            // Materialise lists so we can safely truncate them
            var taskCreated = tasksBuckets.Created.ToList();
            var taskUpdated = tasksBuckets.Updated.ToList();
            var taskDeleted = tasksBuckets.Deleted.ToList();

            var noteCreated = notesBuckets.Created.ToList();
            var noteUpdated = notesBuckets.Updated.ToList();
            var noteDeleted = notesBuckets.Deleted.ToList();

            var totalTaskChanges = taskCreated.Count + taskUpdated.Count + taskDeleted.Count;
            var totalNoteChanges = noteCreated.Count + noteUpdated.Count + noteDeleted.Count;

            var hasMoreTasks = totalTaskChanges > effectiveMax;
            var hasMoreNotes = totalNoteChanges > effectiveMax;

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

            var dto = new SyncChangesDto
            {
                ServerTimestampUtc = serverTimestampUtc,
                Tasks = limitedTasksBuckets,
                Notes = limitedNotesBuckets,
                HasMoreTasks = hasMoreTasks,
                HasMoreNotes = hasMoreNotes
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
