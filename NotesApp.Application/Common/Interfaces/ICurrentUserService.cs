using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Common.Interfaces
{
    /// <summary>
    /// Abstraction for getting the current user's internal Id (User.Id)
    /// inside Application layer (handlers, services).
    ///
    /// The implementation lives in the Infrastructure/API layer and
    /// uses HttpContext + the User/UserLogins tables.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>
        /// Returns the current authenticated user Id (internal GUID).
        /// Throws if there is no authenticated user.
        /// </summary>
        Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default);
    }
}
