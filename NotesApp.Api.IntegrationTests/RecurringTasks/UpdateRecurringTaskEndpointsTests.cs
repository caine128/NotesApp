using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.RecurringTasks
{
    /// <summary>
    /// End-to-end HTTP tests for the recurring-task update endpoints:
    ///   PUT /api/tasks/{taskId}/recurring                  (materialized — Single / All / ThisAndFollowing)
    ///   PUT /api/tasks/virtual-occurrences                 (virtual — Single)
    ///   GET /api/tasks/virtual-occurrences/detail          (projection from series + exception)
    /// </summary>
    public sealed class UpdateRecurringTaskEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public UpdateRecurringTaskEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------
        // Seed helper
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates a recurring task anchored in the past (2026-01-05) with COUNT=5
        /// so the materializer fully expands occurrences into TaskItems.
        /// Returns the seeded series, the materialized tasks (ordered by Date),
        /// and the user's id.
        /// </summary>
        private async Task<(Guid userId, RecurringTaskRoot root, RecurringTaskSeries series, List<TaskItem> tasks, HttpClient client)>
            SeedDailySeriesAsync(int count = 5, object[]? templateSubtasks = null)
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2026, 1, 5);

            var payload = new
            {
                Date = startDate,
                Title = "Original title",
                Description = "Original description",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 30),
                RecurrenceRule = new
                {
                    RRuleString = $"FREQ=DAILY;COUNT={count}",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = templateSubtasks
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.Created);

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
        // PUT scope = All
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_recurring_scope_All_updates_series_template_and_materialized_tasks()
        {
            var (userId, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var firstTask = tasks[0];

            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = firstTask.CanonicalOccurrenceDate!.Value,
                Title = "Renamed title",
                Description = "New description",
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(10, 45),
                Priority = 3,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{firstTask.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var updatedSeries = await db.RecurringTaskSeries.AsNoTracking().SingleAsync(s => s.Id == series.Id);
            updatedSeries.Title.Should().Be("Renamed title");
            updatedSeries.Description.Should().Be("New description");
            updatedSeries.StartTime.Should().Be(new TimeOnly(10, 0));
            updatedSeries.EndTime.Should().Be(new TimeOnly(10, 45));
            updatedSeries.Priority.Should().Be(TaskPriority.High);
            updatedSeries.Version.Should().BeGreaterThan(series.Version);

            // Every materialized TaskItem (no exceptions yet) should have its template fields refreshed.
            var refreshed = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId).ToListAsync();
            refreshed.Should().OnlyContain(t => t.Title == "Renamed title");
            refreshed.Should().OnlyContain(t => t.StartTime == new TimeOnly(10, 0));
            refreshed.Should().OnlyContain(t => t.Priority == TaskPriority.High);

            // No exceptions are created by an All-scope update.
            (await db.RecurringTaskExceptions.AsNoTracking().CountAsync(e => e.UserId == userId))
                .Should().Be(0);
        }

        [Fact]
        public async Task Update_recurring_scope_All_with_individually_modified_occurrence_skips_that_task()
        {
            var (userId, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var pinnedTask = tasks[1];
            var pinnedDate = pinnedTask.CanonicalOccurrenceDate!.Value;

            // First — make a Single-scope override on the second occurrence.
            var singleBody = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = pinnedDate,
                Title = "Pinned override",
                Priority = 3,
                IsCompleted = false
            };
            var singleResponse = await client.PutAsJsonAsync($"/api/tasks/{pinnedTask.Id}/recurring", singleBody);
            singleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Now do an All-scope update.
            var allBody = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = tasks[0].CanonicalOccurrenceDate!.Value,
                Title = "Bulk title",
                Priority = 2,
                IsCompleted = false
            };
            var response = await client.PutAsJsonAsync($"/api/tasks/{tasks[0].Id}/recurring", allBody);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pinnedRow = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == pinnedTask.Id);
            // The pinned task was individually modified so the All-scope update must skip it.
            pinnedRow.Title.Should().Be("Pinned override");

            // Other materialized tasks were updated.
            var others = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.Id != pinnedTask.Id).ToListAsync();
            others.Should().OnlyContain(t => t.Title == "Bulk title");
        }

        // -----------------------------------------------------------------
        // PUT scope = ThisAndFollowing
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_recurring_scope_ThisAndFollowing_terminates_old_series_and_creates_new_segment()
        {
            var (userId, root, series, tasks, client) = await SeedDailySeriesAsync(count: 5);
            var splitTask = tasks[2];
            var splitDate = splitTask.CanonicalOccurrenceDate!.Value;

            var body = new
            {
                Scope = "ThisAndFollowing",
                SeriesId = series.Id,
                OccurrenceDate = splitDate,
                Title = "New segment title",
                Description = "New segment desc",
                StartTime = new TimeOnly(11, 0),
                EndTime = new TimeOnly(11, 30),
                Priority = 2,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{splitTask.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Old series terminated at splitDate.
            var oldSeries = await db.RecurringTaskSeries.AsNoTracking().SingleAsync(s => s.Id == series.Id);
            oldSeries.EndsBeforeDate.Should().Be(splitDate);

            // A second series exists for the same root.
            var allSeries = await db.RecurringTaskSeries.AsNoTracking()
                .Where(s => s.RootId == root.Id).ToListAsync();
            allSeries.Should().HaveCount(2);

            var newSeries = allSeries.Single(s => s.Id != series.Id);
            newSeries.Title.Should().Be("New segment title");
            newSeries.StartsOnDate.Should().Be(splitDate);
            newSeries.RRuleString.Should().Be(series.RRuleString); // carried forward

            // Materialized TaskItems on or after splitDate from the OLD series must be soft-deleted.
            var oldTasksFromSplit = await db.Tasks.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.RecurringSeriesId == series.Id
                            && t.CanonicalOccurrenceDate >= splitDate)
                .ToListAsync();
            oldTasksFromSplit.Should().OnlyContain(t => t.IsDeleted);

            // Old-series tasks before splitDate untouched.
            var oldTasksBeforeSplit = await db.Tasks.AsNoTracking()
                .Where(t => t.RecurringSeriesId == series.Id).ToListAsync();
            oldTasksBeforeSplit.Should().OnlyContain(t => !t.IsDeleted);
            oldTasksBeforeSplit.Should().OnlyContain(t => t.CanonicalOccurrenceDate < splitDate);

            // New series should have its own materialized TaskItems with the new title.
            var newTasks = await db.Tasks.AsNoTracking()
                .Where(t => t.RecurringSeriesId == newSeries.Id).ToListAsync();
            newTasks.Should().NotBeEmpty();
            newTasks.Should().OnlyContain(t => t.Title == "New segment title");
            newTasks.Should().OnlyContain(t => t.StartTime == new TimeOnly(11, 0));
        }

        [Fact]
        public async Task Update_recurring_scope_ThisAndFollowing_with_new_rrule_uses_new_rrule_for_new_series()
        {
            var (_, root, series, tasks, client) = await SeedDailySeriesAsync(count: 5);
            var splitTask = tasks[2];

            var body = new
            {
                Scope = "ThisAndFollowing",
                SeriesId = series.Id,
                OccurrenceDate = splitTask.CanonicalOccurrenceDate!.Value,
                Title = "Weekly now",
                Priority = 2,
                IsCompleted = false,
                NewRRuleString = "FREQ=WEEKLY;COUNT=2"
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{splitTask.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newSeries = await db.RecurringTaskSeries.AsNoTracking()
                .SingleAsync(s => s.RootId == root.Id && s.Id != series.Id);
            newSeries.RRuleString.Should().Be("FREQ=WEEKLY;COUNT=2");
        }

        [Fact]
        public async Task Update_recurring_scope_ThisAndFollowing_with_invalid_rrule_returns_400()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);

            var body = new
            {
                Scope = "ThisAndFollowing",
                SeriesId = series.Id,
                OccurrenceDate = tasks[1].CanonicalOccurrenceDate!.Value,
                Title = "Bad rule",
                Priority = 2,
                IsCompleted = false,
                NewRRuleString = "COUNT=3" // missing FREQ=
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{tasks[1].Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // -----------------------------------------------------------------
        // PUT scope = Single (materialized)
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_recurring_scope_Single_on_materialized_updates_task_and_creates_exception()
        {
            var (userId, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var target = tasks[1];
            var occurrenceDate = target.CanonicalOccurrenceDate!.Value;

            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "One-off override",
                Description = "Special",
                Location = "Room 9",
                Priority = 3,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // TaskItem updated.
            var updated = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == target.Id);
            updated.Title.Should().Be("One-off override");
            updated.Location.Should().Be("Room 9");
            updated.Priority.Should().Be(TaskPriority.High);

            // Exception created and linked to the materialized task.
            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == series.Id && e.OccurrenceDate == occurrenceDate);
            ex.IsDeletion.Should().BeFalse();
            ex.OverrideTitle.Should().Be("One-off override");
            ex.OverrideLocation.Should().Be("Room 9");
            ex.OverridePriority.Should().Be(TaskPriority.High);
            ex.MaterializedTaskItemId.Should().Be(target.Id);

            // Other tasks in the series are NOT touched.
            var others = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.Id != target.Id).ToListAsync();
            others.Should().OnlyContain(t => t.Title == "Original title");
        }

        [Fact]
        public async Task Update_recurring_scope_Single_twice_updates_existing_exception_in_place()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var target = tasks[1];
            var occurrenceDate = target.CanonicalOccurrenceDate!.Value;

            var first = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "Override v1",
                Priority = 2,
                IsCompleted = false
            };
            (await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", first))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var second = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "Override v2",
                Priority = 3,
                IsCompleted = true
            };
            (await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", second))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var allEx = await db.RecurringTaskExceptions.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.SeriesId == series.Id && e.OccurrenceDate == occurrenceDate).ToListAsync();
            allEx.Should().HaveCount(1);
            allEx[0].OverrideTitle.Should().Be("Override v2");
            allEx[0].OverridePriority.Should().Be(TaskPriority.High);
            allEx[0].IsCompleted.Should().BeTrue();
            allEx[0].Version.Should().BeGreaterThan(1);
        }

        // -----------------------------------------------------------------
        // PUT virtual-occurrences (Single, no TaskItem)
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_virtual_occurrence_Single_creates_exception_only_no_task_modification()
        {
            // Use a series that extends past today so we have virtual occurrences to override.
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2027, 1, 4); // future — engine won't materialize past horizon
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
            using (var seedScope = _factory.Services.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var login = await db.UserLogins.AsNoTracking()
                    .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
                var internalUserId = login.UserId;
                // Confirm we have a series; tasks may or may not be materialized depending on horizon.
                (await db.RecurringTaskSeries.AsNoTracking().AnyAsync(s => s.UserId == internalUserId))
                    .Should().BeTrue();
                seriesId = (await db.RecurringTaskSeries.AsNoTracking()
                    .SingleAsync(x => x.UserId == internalUserId)).Id;
            }

            var virtualDate = startDate.AddDays(8); // far enough out to be virtual

            var body = new
            {
                Scope = "Single",
                SeriesId = seriesId,
                OccurrenceDate = virtualDate,
                Title = "Virtual override",
                Priority = 3,
                IsCompleted = true
            };

            var response = await client.PutAsJsonAsync("/api/tasks/virtual-occurrences", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ex = await db2.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == virtualDate);
            ex.OverrideTitle.Should().Be("Virtual override");
            ex.OverridePriority.Should().Be(TaskPriority.High);
            ex.IsCompleted.Should().BeTrue();
            ex.MaterializedTaskItemId.Should().BeNull();

            // No materialized TaskItem on the override date.
            var taskOnDate = await db2.Tasks.AsNoTracking()
                .CountAsync(t => t.RecurringSeriesId == seriesId && t.CanonicalOccurrenceDate == virtualDate);
            taskOnDate.Should().Be(0);
        }

        // -----------------------------------------------------------------
        // GET /virtual-occurrences/detail
        // -----------------------------------------------------------------

        [Fact]
        public async Task Get_virtual_occurrence_detail_with_no_exception_returns_series_template_fields()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var someDate = tasks[0].CanonicalOccurrenceDate!.Value;

            var response = await client.GetAsync(
                $"/api/tasks/virtual-occurrences/detail?seriesId={series.Id}&date={someDate:yyyy-MM-dd}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
            dto.Should().NotBeNull();
            dto!.Title.Should().Be("Original title");
            dto.Description.Should().Be("Original description");
            dto.StartTime.Should().Be(new TimeOnly(9, 0));
            dto.EndTime.Should().Be(new TimeOnly(9, 30));
            dto.Date.Should().Be(someDate);
        }

        [Fact]
        public async Task Get_virtual_occurrence_detail_with_override_exception_returns_override_fields()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 4);
            var target = tasks[2];
            var occurrenceDate = target.CanonicalOccurrenceDate!.Value;

            // Create an override exception via Single-scope edit.
            var overrideBody = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "Detailed override",
                Description = "Override description",
                Priority = 3,
                IsCompleted = false
            };
            (await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", overrideBody))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var response = await client.GetAsync(
                $"/api/tasks/virtual-occurrences/detail?seriesId={series.Id}&date={occurrenceDate:yyyy-MM-dd}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
            dto.Should().NotBeNull();
            dto!.Title.Should().Be("Detailed override");
            dto.Description.Should().Be("Override description");
            dto.Priority.Should().Be(TaskPriority.High);
        }

        // -----------------------------------------------------------------
        // Validator failures
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_recurring_with_title_too_long_returns_400()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Title = new string('x', 201), // > 200 chars
                Priority = 2,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Update_recurring_with_endtime_before_starttime_returns_400()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Title = "Reasonable",
                StartTime = new TimeOnly(10, 0),
                EndTime = new TimeOnly(9, 0), // earlier than start
                Priority = 2,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Update_recurring_with_empty_title_returns_400()
        {
            var (_, _, series, tasks, client) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Title = "",
                Priority = 2,
                IsCompleted = false
            };

            var response = await client.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // -----------------------------------------------------------------
        // Ownership / not-found
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_recurring_with_other_users_task_returns_404()
        {
            var (_, _, series, tasks, _) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());

            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Title = "Hijack",
                Priority = 2,
                IsCompleted = false
            };

            // Tasks.NotFound → mapped to 404 by NotesAppResultEndpointProfile.
            var response = await attackerClient.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Update_virtual_occurrence_with_other_users_series_returns_failure_and_writes_no_exception()
        {
            var (_, _, series, tasks, _) = await SeedDailySeriesAsync(count: 3);
            var occurrenceDate = tasks[0].CanonicalOccurrenceDate!.Value;

            var attackerId = Guid.NewGuid();
            var attackerClient = _factory.CreateClientAsUser(attackerId);

            var body = new
            {
                Scope = "Single",
                SeriesId = series.Id,
                OccurrenceDate = occurrenceDate,
                Title = "Hijack virtual",
                Priority = 2,
                IsCompleted = false
            };

            var response = await attackerClient.PutAsJsonAsync("/api/tasks/virtual-occurrences", body);
            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskExceptions.AsNoTracking()
                .CountAsync(e => e.SeriesId == series.Id))
                .Should().Be(0);
        }

        [Fact]
        public async Task Update_recurring_without_auth_returns_401()
        {
            var (_, _, series, tasks, _) = await SeedDailySeriesAsync(count: 3);
            var target = tasks[0];

            var anonClient = _factory.CreateClient();
            var body = new
            {
                Scope = "All",
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Title = "Anon",
                Priority = 2,
                IsCompleted = false
            };

            var response = await anonClient.PutAsJsonAsync($"/api/tasks/{target.Id}/recurring", body);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
