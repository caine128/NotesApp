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
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
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
                .Where(t => t.UserId == userId && t.Date == date)
                .OrderBy(t => t.ReminderAtUtc ?? DateTime.MaxValue)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<DayTasksOverviewDto>> GetOverviewForDateRangeAsync(
            Guid userId,
            DateOnly fromInclusive,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default)
        {
            // This is a read-only, aggregated query: use AsNoTracking for performance.
            return await _context.Tasks
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    !t.IsDeleted &&
                    t.Date >= fromInclusive &&
                    t.Date < toExclusive)
                .GroupBy(t => t.Date)
                .Select(g => new DayTasksOverviewDto
                {
                    Date = g.Key,
                    TotalTasks = g.Count(),
                    CompletedTasks = g.Count(t => t.IsCompleted),
                    HasAnyReminder = g.Any(t => t.ReminderAtUtc != null)
                })
                .OrderBy(x => x.Date)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<MonthTasksOverviewDto>> GetYearOverviewAsync(Guid userId,
                                                                                     int year,
                                                                                     CancellationToken cancellationToken = default)
        {
            // Aggregate per month for the specified year and user.
            // EF Core 8+ supports DateOnly and translates Year/Month properties to SQL properly.
            var results = await _context.Tasks
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    !t.IsDeleted &&
                    t.Date.Year == year)
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new MonthTasksOverviewDto
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalTasks = g.Count(),
                    CompletedTasks = g.Count(t => t.IsCompleted),
                    PendingTasks = g.Count() - g.Count(t => t.IsCompleted)
                })
                .OrderBy(x => x.Month)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return results;
        }
    }
}
