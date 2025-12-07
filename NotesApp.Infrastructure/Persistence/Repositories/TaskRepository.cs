using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tasks;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    public sealed class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _context;

        public TaskRepository(AppDbContext context)
        {
            _context = context;
        }

        // Generic repository methods

        public async Task<TaskItem?> GetByIdAsync(Guid id,
                                                  CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }

        public async Task AddAsync(TaskItem entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.Tasks.AddAsync(entity, cancellationToken);
        }

        public void Update(TaskItem entity)
        {
            _context.Tasks.Update(entity);
        }

        public void Remove(TaskItem entity)
        {
            _context.Tasks.Remove(entity);
        }


        // Task-specific query methods

        public async Task<IReadOnlyList<TaskItem>> GetForDayAsync(Guid userId,
                                                                  DateOnly date,
                                                                  CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.Date == date
                            && !t.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TaskItem>> GetForDateRangeAsync(Guid userId,
                                                                        DateOnly fromInclusive,
                                                                        DateOnly toExclusive,
                                                                        CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .Where(t => t.UserId == userId
                            && t.Date >= fromInclusive
                            && t.Date < toExclusive
                            && !t.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TaskItem>> GetChangedSinceAsync(Guid userId,
                                                                        DateTime? since,
                                                                        CancellationToken cancellationToken = default)
        {
            if (since is null)
            {
                // Initial sync: all non-deleted tasks for the user.
                return await _context.Tasks
                    .Where(t => t.UserId == userId && !t.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            // Incremental sync: include soft-deleted tasks as well.
            return await _context.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.UpdatedAtUtc > since.Value)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<TaskItem>> GetOverdueRemindersAsync(DateTime utcNow,
                                                                   int maxResults,
                                                                   CancellationToken cancellationToken = default)
        {
            if (maxResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults,
                    "maxResults must be greater than zero.");
            }

            // A reminder is "overdue" when:
            // - The task is not soft-deleted
            // - ReminderAtUtc is set and <= utcNow
            // - ReminderSentAtUtc is null (we haven't sent it yet)
            // - ReminderAcknowledgedAtUtc is null (user hasn't acknowledged)
            return await _context.Tasks
                .Where(t =>
                    !t.IsDeleted &&
                    t.ReminderAtUtc != null &&
                    t.ReminderAtUtc <= utcNow &&
                    t.ReminderSentAtUtc == null &&
                    t.ReminderAcknowledgedAtUtc == null)
                .OrderBy(t => t.ReminderAtUtc)
                .Take(maxResults)
                .ToListAsync(cancellationToken);
        }

    }
}
