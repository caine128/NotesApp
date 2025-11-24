using NotesApp.Application.Tasks;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface ITaskRepository : IRepository<TaskItem>
    {
        Task<IReadOnlyList<TaskItem>> GetForDayAsync(Guid userId,
                                                     DateOnly date,
                                                     CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns aggregated counts of tasks per day for a given user over a date range.
        /// The range is [fromInclusive, toExclusive).
        /// </summary>
        Task<IReadOnlyList<DayTasksOverviewDto>> GetOverviewForDateRangeAsync(Guid userId,
                                                                              DateOnly fromInclusive,
                                                                              DateOnly toExclusive,
                                                                              CancellationToken cancellationToken = default);


        /// <summary>
        /// Returns aggregated counts of tasks per month for a given user and year.
        /// Only non-deleted tasks are included.
        /// </summary>
        Task<IReadOnlyList<MonthTasksOverviewDto>> GetYearOverviewAsync(Guid userId,
                                                                        int year,
                                                                        CancellationToken cancellationToken = default);


        // TODO : Later you can add:
        // Task<IReadOnlyList<TaskItem>> GetForMonthAsync(Guid userId, int year, int month, CancellationToken ct = default);
    }
}
