using FluentResults;
using MediatR;
using System;

namespace NotesApp.Application.Categories.Commands.DeleteTaskCategory
{
    /// <summary>
    /// Soft-deletes a user-defined task category.
    ///
    /// After the category is soft-deleted, all tasks that reference it via
    /// CategoryId are immediately bulk-updated on the server (CategoryId set to null,
    /// Version incremented). This ensures clients pick up the change on the next
    /// sync pull without requiring them to send explicit task updates.
    ///
    /// Note: this handler is only called from the REST/web path. In the sync push
    /// path, mobile clients send the affected task updates themselves.
    /// </summary>
    public sealed class DeleteTaskCategoryCommand : IRequest<Result>
    {
        /// <summary>The server-side id of the category to delete. Set from route by the controller.</summary>
        public Guid CategoryId { get; set; }

        // REFACTORED: added RowVersion for web concurrency protection
        public byte[] RowVersion { get; init; } = [];
    }
}
