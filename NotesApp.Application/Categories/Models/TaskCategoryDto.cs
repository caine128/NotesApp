using System;

namespace NotesApp.Application.Categories.Models
{
    /// <summary>
    /// Represents a user-defined task category returned by the API.
    /// </summary>
    /// <param name="CategoryId">The unique identifier of the category.</param>
    /// <param name="Name">The display name of the category.</param>
    /// <param name="Version">Monotonically increasing version for sync conflict detection.</param>
    /// <param name="CreatedAtUtc">UTC timestamp when the category was created.</param>
    /// <param name="UpdatedAtUtc">UTC timestamp of the last update.</param>
    public sealed record TaskCategoryDto(
        Guid CategoryId,
        string Name,
        long Version,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
