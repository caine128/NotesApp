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

        // TODO : Later you can add:
        // Task<IReadOnlyList<TaskItem>> GetForMonthAsync(Guid userId, int year, int month, CancellationToken ct = default);
    }
}
