using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;

namespace NotesApp.Application.Tests.Categories
{
    /// <summary>
    /// Integration tests for CategoryRepository using a real SQL Server LocalDB database.
    ///
    /// Covers:
    /// - GetChangedSinceAsync: initial (null) and incremental paths, user isolation,
    ///   soft-delete visibility, and threshold boundary.
    /// - GetAllForUserAsync: alphabetical ordering, user isolation, soft-delete exclusion.
    /// - GetByIdUntrackedAsync: global query filter hides soft-deleted rows.
    /// </summary>
    public sealed class CategoryRepositoryTests
    {
        private static TaskCategory SeedCategory(Guid userId, string name, DateTime utcNow)
        {
            var result = TaskCategory.Create(userId, name, utcNow);
            result.IsSuccess.Should().BeTrue("test setup must produce a valid TaskCategory");
            return result.Value!;
        }

        /// <summary>
        /// Soft-deletes a category in the DB via the tracked entity.
        /// The entity must already be in the change tracker (i.e. seeded via the same context).
        /// </summary>
        private static async Task SoftDeleteAsync(
            AppDbContext context,
            TaskCategory category,
            DateTime utcNow)
        {
            // Re-fetch via IgnoreQueryFilters so we get the tracked instance regardless of filter.
            // Then apply domain SoftDelete and persist via Update.
            var tracked = await context.TaskCategories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == category.Id);

            var deleteResult = tracked.SoftDelete(utcNow);
            deleteResult.IsSuccess.Should().BeTrue("test setup: soft-delete should succeed on a live category");

            context.TaskCategories.Update(tracked);
            await context.SaveChangesAsync();
        }

        // -----------------------------------------------------------------------
        // GetChangedSinceAsync — initial sync (sinceUtc = null)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task GetChangedSinceAsync_initial_sync_returns_all_non_deleted_categories_for_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var cat1 = SeedCategory(userId, "Work", now);
            var cat2 = SeedCategory(userId, "Personal", now);

            await context.TaskCategories.AddRangeAsync(cat1, cat2);
            await context.SaveChangesAsync();

            var result = await repo.GetChangedSinceAsync(userId, null, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Select(c => c.Id).Should().BeEquivalentTo(new[] { cat1.Id, cat2.Id });
        }

        [Fact]
        public async Task GetChangedSinceAsync_initial_sync_excludes_soft_deleted_categories()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var liveCategory = SeedCategory(userId, "Work", now);
            var deletedCategory = SeedCategory(userId, "Personal", now);

            await context.TaskCategories.AddRangeAsync(liveCategory, deletedCategory);
            await context.SaveChangesAsync();

            await SoftDeleteAsync(context, deletedCategory, now.AddMinutes(1));

            var result = await repo.GetChangedSinceAsync(userId, null, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(liveCategory.Id);
        }

        [Fact]
        public async Task GetChangedSinceAsync_initial_sync_isolates_categories_by_user()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var userCategory = SeedCategory(userId, "Work", now);
            var otherCategory = SeedCategory(otherUserId, "Finance", now);

            await context.TaskCategories.AddRangeAsync(userCategory, otherCategory);
            await context.SaveChangesAsync();

            var result = await repo.GetChangedSinceAsync(userId, null, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(userCategory.Id);
        }

        // -----------------------------------------------------------------------
        // GetChangedSinceAsync — incremental sync (sinceUtc set)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task GetChangedSinceAsync_incremental_returns_only_categories_updated_after_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Created an hour before since — should NOT appear.
            var oldCategory = SeedCategory(userId, "Old", since.AddHours(-1));

            // Created an hour after since — should appear.
            var newCategory = SeedCategory(userId, "New", since.AddHours(1));

            await context.TaskCategories.AddRangeAsync(oldCategory, newCategory);
            await context.SaveChangesAsync();

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(newCategory.Id);
        }

        [Fact]
        public async Task GetChangedSinceAsync_incremental_includes_soft_deleted_categories_updated_after_since()
        {
            // Soft-deleted categories are surfaced by IgnoreQueryFilters() so the caller
            // can bucket them into the "deleted" collection of the sync response.
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Created before since, then soft-deleted after since.
            var category = SeedCategory(userId, "Work", since.AddHours(-1));
            await context.TaskCategories.AddAsync(category);
            await context.SaveChangesAsync();

            // Soft-delete updates UpdatedAtUtc to now = since + 1 hour → appears in result.
            await SoftDeleteAsync(context, category, since.AddHours(1));

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(category.Id);
            result[0].IsDeleted.Should().BeTrue();
        }

        [Fact]
        public async Task GetChangedSinceAsync_incremental_excludes_categories_not_changed_after_since()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var since = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            // Created two hours before since — UpdatedAtUtc is also before since.
            var staleCategory = SeedCategory(userId, "Stale", since.AddHours(-2));
            await context.TaskCategories.AddAsync(staleCategory);
            await context.SaveChangesAsync();

            var result = await repo.GetChangedSinceAsync(userId, since, CancellationToken.None);

            result.Should().BeEmpty();
        }

        // -----------------------------------------------------------------------
        // GetAllForUserAsync
        // -----------------------------------------------------------------------

        [Fact]
        public async Task GetAllForUserAsync_returns_categories_alphabetically_ordered_for_user_only()
        {
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var otherUserId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var catZ = SeedCategory(userId, "Zzz", now);
            var catA = SeedCategory(userId, "Aardvark", now);
            var catM = SeedCategory(userId, "Misc", now);
            var softDeletedCat = SeedCategory(userId, "Archived", now);
            var otherUserCat = SeedCategory(otherUserId, "Other", now);

            await context.TaskCategories.AddRangeAsync(catZ, catA, catM, softDeletedCat, otherUserCat);
            await context.SaveChangesAsync();

            await SoftDeleteAsync(context, softDeletedCat, now.AddMinutes(1));

            var result = await repo.GetAllForUserAsync(userId, CancellationToken.None);

            result.Should().HaveCount(3);
            result[0].Name.Should().Be("Aardvark");
            result[1].Name.Should().Be("Misc");
            result[2].Name.Should().Be("Zzz");
        }

        // -----------------------------------------------------------------------
        // GetByIdUntrackedAsync — global query filter
        // -----------------------------------------------------------------------

        [Fact]
        public async Task GetByIdUntrackedAsync_returns_null_when_category_is_soft_deleted()
        {
            // The global query filter [!IsDeleted] hides soft-deleted categories from
            // all normal reads, including untracked loads.  This is important because:
            //  - SyncPush delete handler relies on null → NotFound (not AlreadyDeleted)
            //  - REST delete idempotency path relies on null → safe retry
            await using var context = SqlServerAppDbContextFactory.CreateContext();
            var repo = new CategoryRepository(context);

            var userId = Guid.NewGuid();
            var now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var category = SeedCategory(userId, "Work", now);
            await context.TaskCategories.AddAsync(category);
            await context.SaveChangesAsync();

            // Confirm it is findable before soft-delete.
            var foundBefore = await repo.GetByIdUntrackedAsync(category.Id, CancellationToken.None);
            foundBefore.Should().NotBeNull();

            await SoftDeleteAsync(context, category, now.AddMinutes(1));

            // After soft-delete the category must no longer be visible.
            var foundAfter = await repo.GetByIdUntrackedAsync(category.Id, CancellationToken.None);
            foundAfter.Should().BeNull("global query filter must hide soft-deleted categories");
        }
    }
}
