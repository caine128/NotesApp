using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.RecurringTasks
{
    /// <summary>
    /// End-to-end HTTP tests for the recurring-task delete endpoints:
    ///   DELETE /api/tasks/{taskId}/recurring        (materialized — Single / All / ThisAndFollowing)
    ///   DELETE /api/tasks/virtual-occurrences        (virtual — Single)
    /// </summary>
    public sealed class DeleteRecurringTaskEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public DeleteRecurringTaskEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        private async Task<(Guid userId, RecurringTaskRoot root, RecurringTaskSeries series, List<TaskItem> tasks, HttpClient client)>
            SeedDailySeriesAsync(int count = 5)
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2026, 1, 5);

            var payload = new
            {
                Date = startDate,
                Title = "Deletable",
                RecurrenceRule = new
                {
                    RRuleString = $"FREQ=DAILY;COUNT={count}",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            (await client.PostAsJsonAsync("/api/tasks", payload))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var login = await db.UserLogins.AsNoTracking()
                .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
            var internalUserId = login.UserId;
            var root = await db.RecurringTaskRoots.AsNoTracking().SingleAsync(r => r.UserId == internalUserId);
            var series = await db.RecurringTaskSeries.AsNoTracking().SingleAsync(s => s.UserId == internalUserId);
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == internalUserId)
                .OrderBy(t => t.Date)
                .ToListAsync();
            return (internalUserId, root, series, tasks, client);
        }

        // -----------------------------------------------------------------
        // DELETE scope = Single (materialized)
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_recurring_scope_Single_on_materialized_soft_deletes_task_and_creates_deletion_exception()
        {
            var (userId, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var target = tasks[1];
            var occurrenceDate = target.CanonicalOccurrenceDate!.Value;

            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate
            };

            var response = await client.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // TaskItem is soft-deleted.
            var taskRow = await db.Tasks.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(t => t.Id == target.Id);
            taskRow.IsDeleted.Should().BeTrue();

            // Deletion exception created and linked to the materialized TaskItem.
            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == series.Id && e.OccurrenceDate == occurrenceDate);
            ex.IsDeletion.Should().BeTrue();
            ex.MaterializedTaskItemId.Should().Be(target.Id);

            // Sibling tasks unaffected.
            var siblings = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.Id != target.Id).ToListAsync();
            siblings.Should().HaveCount(tasks.Count - 1);
            siblings.Should().OnlyContain(t => !t.IsDeleted);
        }

        [Fact]
        public async Task Delete_recurring_scope_Single_after_override_converts_existing_exception_in_place()
        {
            // Edit-then-delete on the same occurrence must not leave duplicate exception rows.
            // The override exception is converted in place (IsDeletion=true, override fields cleared)
            // rather than soft-deleted alongside a new deletion row.
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var target = tasks[1];
            var occurrenceDate = target.CanonicalOccurrenceDate!.Value;

            // Step 1 — create an override exception via Single update.
            var updateBody = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "Pre-deletion override",
                Priority = 2,
                IsCompleted = false
            };
            (await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", updateBody))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            // Step 2 — delete the occurrence.
            var deleteBody = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate
            };
            (await client.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", deleteBody))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Exactly one row exists across (live + soft-deleted) for this (SeriesId, OccurrenceDate),
            // and it is the live deletion. Override fields are cleared; Version was incremented.
            var allExceptions = await db.RecurringTaskExceptions.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.SeriesId == series.Id && e.OccurrenceDate == occurrenceDate)
                .ToListAsync();
            allExceptions.Should().HaveCount(1);

            var only = allExceptions[0];
            only.IsDeletion.Should().BeTrue();
            only.IsDeleted.Should().BeFalse();
            only.OverrideTitle.Should().BeNull();
            only.MaterializedTaskItemId.Should().Be(target.Id);
            only.Version.Should().BeGreaterThan(1);
        }

        [Fact]
        public async Task Delete_recurring_scope_Single_idempotent_on_already_deleted_returns_204()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value
            };

            (await client.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", body))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Second delete — handler short-circuits when an existing deletion exception is found.
            var second = await client.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // -----------------------------------------------------------------
        // DELETE virtual-occurrences (Single)
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_virtual_occurrence_Single_creates_deletion_exception_only()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2027, 1, 4);
            var payload = new
            {
                Date = startDate,
                Title = "Future series",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=10",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };
            (await client.PostAsJsonAsync("/api/tasks", payload))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            Guid seriesId;
            using (var s = _factory.Services.CreateScope())
            {
                var db0 = s.ServiceProvider.GetRequiredService<AppDbContext>();
                var login = await db0.UserLogins.AsNoTracking()
                    .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
                seriesId = (await db0.RecurringTaskSeries.AsNoTracking()
                    .SingleAsync(x => x.UserId == login.UserId)).Id;
            }
            var virtualDate = startDate.AddDays(8);

            var body = new
            {
                Scope = "Single",
                SeriesId = seriesId,
                OccurrenceDate = virtualDate
            };

            var response = await client.DeleteAsJsonAsync("/api/tasks/virtual-occurrences", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == virtualDate);
            ex.IsDeletion.Should().BeTrue();
            ex.MaterializedTaskItemId.Should().BeNull();

            // No materialized TaskItem on the override date.
            var taskOnDate = await db.Tasks.AsNoTracking()
                .CountAsync(t => t.RecurringSeriesId == seriesId && t.CanonicalOccurrenceDate == virtualDate);
            taskOnDate.Should().Be(0);
        }

        // -----------------------------------------------------------------
        // DELETE scope = ThisAndFollowing
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_recurring_scope_ThisAndFollowing_terminates_series_and_soft_deletes_future_tasks()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 5);
            var splitTask = tasks[2];
            var splitDate = splitTask.CanonicalOccurrenceDate!.Value;

            var body = new
            {
                Scope = "ThisAndFollowing",
                SeriesId = series.Id,
                OccurrenceDate = splitDate
            };

            (await client.DeleteAsJsonAsync($"/api/tasks/{splitTask.Id}/recurring", body))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var refreshedSeries = await db.RecurringTaskSeries.AsNoTracking()
                .SingleAsync(s => s.Id == series.Id);
            refreshedSeries.EndsBeforeDate.Should().Be(splitDate);

            // Tasks on or after splitDate are soft-deleted.
            var fromSplit = await db.Tasks.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.RecurringSeriesId == series.Id
                            && t.CanonicalOccurrenceDate >= splitDate)
                .ToListAsync();
            fromSplit.Should().NotBeEmpty();
            fromSplit.Should().OnlyContain(t => t.IsDeleted);

            // Tasks before splitDate untouched.
            var beforeSplit = await db.Tasks.AsNoTracking()
                .Where(t => t.RecurringSeriesId == series.Id).ToListAsync();
            beforeSplit.Should().OnlyContain(t => !t.IsDeleted);
            beforeSplit.Should().OnlyContain(t => t.CanonicalOccurrenceDate < splitDate);

            // No new series created (delete does not spawn one).
            var seriesForRoot = await db.RecurringTaskSeries.AsNoTracking()
                .CountAsync(s => s.RootId == series.RootId);
            seriesForRoot.Should().Be(1);
        }

        // -----------------------------------------------------------------
        // DELETE scope = All
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_recurring_scope_All_soft_deletes_root_series_and_tasks()
        {
            var (userId, root, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var anyTask = tasks[0];

            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = anyTask.CanonicalOccurrenceDate!.Value
            };

            (await client.DeleteAsJsonAsync($"/api/tasks/{anyTask.Id}/recurring", body))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (await db.RecurringTaskRoots.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(r => r.Id == root.Id)).IsDeleted.Should().BeTrue();
            (await db.RecurringTaskSeries.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.Id == series.Id)).IsDeleted.Should().BeTrue();

            var allTasks = await db.Tasks.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.UserId == userId).ToListAsync();
            allTasks.Should().NotBeEmpty();
            allTasks.Should().OnlyContain(t => t.IsDeleted);
        }

        // -----------------------------------------------------------------
        // Ownership / auth
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_recurring_with_other_users_task_returns_404()
        {
            var (_, _, series, tasks, _) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value
            };

            // Tasks.NotFound is mapped to 404 by NotesAppResultEndpointProfile.
            var response = await attackerClient.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Delete_recurring_All_with_other_users_series_writes_no_changes()
        {
            var (userId, root, series, tasks, _) = await SeedDailySeriesAsync(count: 3);

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = tasks[0].CanonicalOccurrenceDate!.Value
            };

            var response = await attackerClient.DeleteAsJsonAsync($"/api/tasks/{tasks[0].Id}/recurring", body);
            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskRoots.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(r => r.Id == root.Id)).IsDeleted.Should().BeFalse();
            (await db.RecurringTaskSeries.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.Id == series.Id)).IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_recurring_without_auth_returns_401()
        {
            var (_, _, series, tasks, _) = await SeedDailySeriesAsync(count: 2);
            var target = tasks[0];

            var anonClient = _factory.CreateClient();
            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value
            };

            var response = await anonClient.DeleteAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
