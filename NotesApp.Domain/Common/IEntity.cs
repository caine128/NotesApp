using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Domain.Common
{
    /// <summary>
    /// Marker interface for domain entities with a strongly-typed Id.
    /// </summary>
    /// <typeparam name="TId">Type of the primary key (e.g. Guid).</typeparam>
    public interface IEntity<TId>
    {
        TId Id { get; }
        DateTime CreatedAtUtc { get; }
        DateTime UpdatedAtUtc { get; }
        bool IsDeleted { get; }
        byte[] RowVersion { get; }
    }
}
