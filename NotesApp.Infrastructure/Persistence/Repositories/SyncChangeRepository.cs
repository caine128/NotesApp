using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    public sealed class SyncChangeRepository : ISyncChangeRepository
    {
        private readonly AppDbContext _context;

        public SyncChangeRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(SyncChange change, CancellationToken cancellationToken = default)
        {
            await _context.SyncChanges.AddAsync(change, cancellationToken);
        }

        public async Task<IReadOnlyList<SyncChange>> GetAfterSequenceAsync(Guid userId,
                                                                          long afterSequence,
                                                                          int limit,
                                                                          CancellationToken cancellationToken = default)
        {
            return await _context.SyncChanges
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.Sequence > afterSequence)
                .OrderBy(x => x.Sequence)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<long> GetMinRetainedSequenceAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var state = await _context.SyncSequenceStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            return state?.MinRetainedSequence ?? 0L;
        }

        public async Task<long> GetCurrentMaxSequenceAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var state = await _context.SyncSequenceStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            return state is null ? 0L : state.NextSequence - 1;
        }
    }
}
