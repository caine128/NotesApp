using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    public sealed class OutboxRepository : IOutboxRepository
    {
        private readonly AppDbContext _context;

        public OutboxRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<OutboxMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.OutboxMessages
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        }

        public async Task AddAsync(OutboxMessage entity, CancellationToken cancellationToken = default)
        {
            await _context.OutboxMessages.AddAsync(entity, cancellationToken);
        }

        public void Update(OutboxMessage entity)
        {
            _context.OutboxMessages.Update(entity);
        }

        public void Remove(OutboxMessage entity)
        {
            _context.OutboxMessages.Remove(entity);
        }

        public async Task<IReadOnlyList<OutboxMessage>> GetPendingBatchAsync(
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            return await _context.OutboxMessages
                .Where(o => o.ProcessedAtUtc == null)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(maxCount)
                .ToListAsync(cancellationToken);
        }
    }
}
