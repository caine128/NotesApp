using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.RecurringTasks
{
    /// <summary>
    /// End-to-end HTTP tests for PUT /api/tasks/recurring-occurrences/subtasks
    /// covering Single (materialized + virtual) and All scopes.
    /// </summary>
    public sealed class UpdateRecurringTaskSubtasksEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public UpdateRecurringTaskSubtasksEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        private async Task<(Guid userId, RecurringTaskSeries series, List<TaskItem> tasks, HttpClient client)>
            SeedDailySeriesWithTemplateSubtasksAsync(int count = 3, object[]? templateSubtasks = null)
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2026, 1, 5);

            var payload = new
            {
                Date = startDate,
                Title = "With subtasks",
                RecurrenceRule = new
                {
                    RRuleString = $"FREQ=DAILY;COUNT={count}",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = templateSubtasks ?? new object[]
                    {
                        new { Text = "Step 1", Position = "a0" },
                        new { Text = "Step 2", Position = "a1" }
                    }
                }
            };

            (await client.PostAsJsonAsync("/api/tasks", payload))
                .StatusCode.Should().Be(HttpStatusCode.Created);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var login = await db.UserLogins.AsNoTracking()
                .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
            var internalUserId = login.UserId;
            var series = await db.RecurringTaskSeries.AsNoTracking().SingleAsync(s => s.UserId == internalUserId);
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == internalUserId)
                .OrderBy(t => t.Date)
                .ToListAsync();
            return (internalUserId, series, tasks, client);
        }

        // -----------------------------------------------------------------
        // Single — materialized
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_subtasks_Single_materialized_replaces_taskitem_subtask_rows()
        {
            var (userId, series, tasks, client) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 3);
            var target = tasks[0];

            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "New A", Position = "a0", IsCompleted = false },
                    new { Text = "New B", Position = "a1", IsCompleted = true }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var subtasks = await db.Subtasks.AsNoTracking()
                .Where(s => s.TaskId == target.Id).ToListAsync();
            subtasks.Should().HaveCount(2);
            subtasks.Select(s => s.Text).Should().BeEquivalentTo(new[] { "New A", "New B" });

            // No exception is created — the materialized TaskItem owns the live rows.
            (await db.RecurringTaskExceptions.AsNoTracking()
                .CountAsync(e => e.UserId == userId)).Should().Be(0);
        }

        // -----------------------------------------------------------------
        // Single — virtual
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_subtasks_Single_virtual_creates_exception_with_override_subtasks()
        {
            // Use a future series so we can target a virtual occurrence cleanly.
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2027, 1, 4);
            var payload = new
            {
                Date = startDate,
                Title = "Future virtual",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=10",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = new object[]
                    {
                        new { Text = "Tmpl A", Position = "a0" }
                    }
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
                Scope = 0,
                TaskItemId = (Guid?)null,
                SeriesId = seriesId,
                OccurrenceDate = virtualDate,
                Subtasks = new[]
                {
                    new { Text = "Override 1", Position = "a0", IsCompleted = false },
                    new { Text = "Override 2", Position = "a1", IsCompleted = false }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == virtualDate);
            ex.IsDeletion.Should().BeFalse();

            var exSubtasks = await db.RecurringTaskSubtasks.AsNoTracking()
                .Where(s => s.ExceptionId == ex.Id).ToListAsync();
            exSubtasks.Should().HaveCount(2);
            exSubtasks.Should().OnlyContain(s => s.SeriesId == null);
            exSubtasks.Select(s => s.Text).Should().BeEquivalentTo(new[] { "Override 1", "Override 2" });
        }

        [Fact]
        public async Task Update_subtasks_Single_virtual_with_empty_list_creates_exception_with_zero_rows()
        {
            // Verifies the explicit-empty semantics: empty list = no subtasks for this occurrence,
            // not "fall back to series template".
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2027, 1, 4);
            var payload = new
            {
                Date = startDate,
                Title = "Future virtual",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=10",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = new object[]
                    {
                        new { Text = "Default", Position = "a0" }
                    }
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
                Scope = 0,
                TaskItemId = (Guid?)null,
                SeriesId = seriesId,
                OccurrenceDate = virtualDate,
                Subtasks = Array.Empty<object>()
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == virtualDate);

            (await db.RecurringTaskSubtasks.AsNoTracking()
                .CountAsync(s => s.ExceptionId == ex.Id)).Should().Be(0);

            // The series template is still intact (one row).
            (await db.RecurringTaskSubtasks.AsNoTracking()
                .CountAsync(s => s.SeriesId == seriesId && s.ExceptionId == null)).Should().Be(1);
        }

        // -----------------------------------------------------------------
        // All scope
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_subtasks_All_replaces_template_subtasks_for_active_series()
        {
            var (userId, series, _, client) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 3);

            var body = new
            {
                Scope = 2,
                TaskItemId = (Guid?)null,
                SeriesId = series.Id,
                Subtasks = new[]
                {
                    new { Text = "Replaced X", Position = "a0", IsCompleted = false },
                    new { Text = "Replaced Y", Position = "a1", IsCompleted = false },
                    new { Text = "Replaced Z", Position = "a2", IsCompleted = false }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The new live template subtasks for the series are the replacements.
            var liveTemplate = await db.RecurringTaskSubtasks.AsNoTracking()
                .Where(s => s.SeriesId == series.Id && s.ExceptionId == null)
                .ToListAsync();
            liveTemplate.Should().HaveCount(3);
            liveTemplate.Select(s => s.Text).Should().BeEquivalentTo(new[] { "Replaced X", "Replaced Y", "Replaced Z" });

            // Old template subtasks are soft-deleted (still present when query filters are off).
            var allTemplate = await db.RecurringTaskSubtasks.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.SeriesId == series.Id && s.ExceptionId == null)
                .ToListAsync();
            allTemplate.Should().HaveCountGreaterThan(3);
            allTemplate.Count(s => s.IsDeleted).Should().BeGreaterThan(0);
        }

        // -----------------------------------------------------------------
        // Validator failures
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_subtasks_with_empty_text_returns_400()
        {
            var (_, series, tasks, client) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 2);
            var target = tasks[0];

            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "", Position = "a0", IsCompleted = false }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Update_subtasks_with_empty_position_returns_400()
        {
            var (_, series, tasks, client) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 2);
            var target = tasks[0];

            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "Looks fine", Position = "", IsCompleted = false }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Update_subtasks_with_empty_series_id_returns_400()
        {
            var (_, _, tasks, client) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 2);
            var target = tasks[0];

            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = Guid.Empty,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "ok", Position = "a0", IsCompleted = false }
                }
            };

            var response = await client.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // -----------------------------------------------------------------
        // Ownership / auth
        // -----------------------------------------------------------------

        [Fact]
        public async Task Update_subtasks_with_other_users_task_writes_no_changes()
        {
            var (_, series, tasks, _) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 2);
            var target = tasks[0];

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "Hijack", Position = "a0", IsCompleted = false }
                }
            };

            var response = await attackerClient.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Subtasks.AsNoTracking()
                .CountAsync(s => s.TaskId == target.Id && s.Text == "Hijack"))
                .Should().Be(0);
        }

        [Fact]
        public async Task Update_subtasks_without_auth_returns_401()
        {
            var (_, series, tasks, _) = await SeedDailySeriesWithTemplateSubtasksAsync(count: 2);
            var target = tasks[0];

            var anonClient = _factory.CreateClient();
            var body = new
            {
                Scope = 0,
                TaskItemId = target.Id,
                SeriesId = series.Id,
                OccurrenceDate = target.CanonicalOccurrenceDate!.Value,
                Subtasks = new[]
                {
                    new { Text = "Anon", Position = "a0", IsCompleted = false }
                }
            };

            var response = await anonClient.PutAsJsonAsync("/api/tasks/recurring-occurrences/subtasks", body);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
