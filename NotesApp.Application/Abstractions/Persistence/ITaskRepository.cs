using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Abstractions.Persistence
{
    public interface ITaskRepository
    {
        Task AddAsync(TaskItem task, CancellationToken cancellationToken = default);
        // TODO : later .. methods for getting/updating tasks, queries, etc.
    }
}
