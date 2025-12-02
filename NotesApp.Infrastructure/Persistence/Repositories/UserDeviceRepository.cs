using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Repositories
{
    public sealed class UserDeviceRepository : IUserDeviceRepository
    {
        private readonly AppDbContext _context;

        public UserDeviceRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<UserDevice?> GetByIdAsync(Guid id,
                                                    CancellationToken cancellationToken = default)
        {
            return await _context.UserDevices
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        }

        public async Task AddAsync(UserDevice entity,
                                   CancellationToken cancellationToken = default)
        {
            await _context.UserDevices.AddAsync(entity, cancellationToken);
        }

        public void Update(UserDevice entity)
        {
            _context.UserDevices.Update(entity);
        }

        public void Remove(UserDevice entity)
        {
            _context.UserDevices.Remove(entity);
        }

        public async Task<UserDevice?> GetByTokenAsync(string deviceToken,
                                                       CancellationToken cancellationToken = default)
        {
            var normalized = deviceToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return await _context.UserDevices
                .IgnoreQueryFilters() // we might want to see deleted devices for migrations
                .FirstOrDefaultAsync(d => d.DeviceToken == normalized, cancellationToken);
        }

        public async Task<IReadOnlyList<UserDevice>> GetActiveDevicesForUserAsync(Guid userId,
                                                                                  CancellationToken cancellationToken = default)
        {
            return await _context.UserDevices
                .Where(d => d.UserId == userId && d.IsActive && !d.IsDeleted)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<UserDevice>> GetActiveDevicesForUserExceptAsync(Guid userId,
                                                                                        Guid excludeDeviceId,
                                                                                        CancellationToken cancellationToken = default)
        {
            return await _context.UserDevices
                .Where(d => d.UserId == userId &&
                            d.IsActive &&
                            !d.IsDeleted &&
                            d.Id != excludeDeviceId)
                .ToListAsync(cancellationToken);
        }
    }
}
