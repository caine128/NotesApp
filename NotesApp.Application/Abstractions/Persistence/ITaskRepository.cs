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
    }
}
