using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    /// <summary>
    /// Persistence abstraction for user devices used in sync and push.
    /// </summary>
    public interface IUserDeviceRepository : IRepository<UserDevice>
    {
        /// <summary>
        /// Finds a device by its token, regardless of user.
        /// </summary>
        Task<UserDevice?> GetByTokenAsync(string deviceToken,
                                          CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a device by its token, regardless of user. WITHOUT change tracking.
        /// Use <see cref="IRepository{TEntity}.Update"/> to persist changes.
        /// </summary>
        Task<UserDevice?> GetByTokenUntrackedAsync(string deviceToken,
                                                   CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all active (non-deleted, IsActive = true) devices for the given user.
        /// </summary>
        Task<IReadOnlyList<UserDevice>> GetActiveDevicesForUserAsync(Guid userId,
                                                                     CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns all active devices for the given user, excluding one device (origin device).
        /// Useful when notifying "other" devices about changes.
        /// </summary>
        Task<IReadOnlyList<UserDevice>> GetActiveDevicesForUserExceptAsync(Guid userId,
                                                                           Guid excludeDeviceId,
                                                                           CancellationToken cancellationToken = default);
    }
}
