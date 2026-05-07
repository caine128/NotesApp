using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;

namespace NotesApp.Infrastructure.Persistence
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;

        public UnitOfWork(AppDbContext context)
        {
            _context = context;
        }

        // Run SaveChanges inside the configured execution strategy so that
        // SyncChangeSequenceInterceptor's manual BeginTransactionAsync is legal under
        // EnableRetryOnFailure and the whole flush (sequence MERGE + EF INSERTs) is one
        // retriable unit.
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(
                _context,
                static (ctx, ct) => ctx.SaveChangesAsync(ct),
                cancellationToken);
        }
    }
}
