using NotesApp.Application.Categories.Models;
using NotesApp.Domain.Entities;
using System.Collections.Generic;
using System.Linq;

namespace NotesApp.Application.Categories
{
    /// <summary>
    /// Extension methods for mapping <see cref="TaskCategory"/> domain entities to DTOs.
    /// Follows the same static extension-method pattern used throughout the application
    /// (see <c>TaskMappings</c> for reference).
    /// </summary>
    public static class CategoryMappings
    {
        /// <summary>Maps a <see cref="TaskCategory"/> to a <see cref="TaskCategoryDto"/>.</summary>
        public static TaskCategoryDto ToDto(this TaskCategory category) =>
            new(category.Id,
                category.Name,
                category.Version,
                category.CreatedAtUtc,
                category.UpdatedAtUtc,
                category.RowVersion); // REFACTORED: added RowVersion for web concurrency protection

        /// <summary>
        /// Maps a collection of <see cref="TaskCategory"/> entities to a read-only list of
        /// <see cref="TaskCategoryDto"/>.
        /// </summary>
        public static IReadOnlyList<TaskCategoryDto> ToDtoList(
            this IEnumerable<TaskCategory> categories) =>
            categories.Select(c => c.ToDto()).ToList();
    }
}
