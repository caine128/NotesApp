using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
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

        public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        }

        public async Task AddAsync(TaskItem entity, CancellationToken cancellationToken = default)
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
    }
}
