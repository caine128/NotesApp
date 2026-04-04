using NotesApp.Application.Tasks;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface ITaskRepository : ICalendarEntityRepository<TaskItem>
    {
        /// <summary>
        /// Returns tasks whose reminder time has passed and for which a reminder
        /// has not yet been sent or acknowledged.
        /// </summary>
        /// <param name="utcNow">Current UTC time used as the "overdue" threshold.</param>
        /// <param name="maxResults">Maximum number of tasks to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<List<TaskItem>> GetOverdueRemindersAsync(DateTime utcNow,
                                                      int maxResults,
                                                      CancellationToken cancellationToken = default);

        /// <summary>
        /// Bulk-clears <c>CategoryId</c> on all non-deleted tasks belonging to the user
        /// that reference the given category. Also increments <c>Version</c> and sets
        /// <c>UpdatedAtUtc</c> so that affected tasks surface in the next sync pull and
        /// any stale mobile push attempts receive a <c>VersionMismatch</c> conflict.
        ///
        /// Called only from <c>DeleteTaskCategoryCommandHandler</c> (REST/web path).
        /// In the sync push path, mobile clients send the affected task updates themselves.
        /// </summary>
        /// <param name="categoryId">The category whose reference should be cleared.</param>
        /// <param name="userId">Owner of the tasks (tenant boundary).</param>
        /// <param name="utcNow">Current UTC time applied to UpdatedAtUtc.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ClearCategoryFromTasksAsync(Guid categoryId,
                                         Guid userId,
                                         DateTime utcNow,
                                         CancellationToken cancellationToken = default);
    }
}
