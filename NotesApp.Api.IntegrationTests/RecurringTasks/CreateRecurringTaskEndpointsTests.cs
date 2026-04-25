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
    /// End-to-end HTTP tests for POST /api/tasks with a RecurrenceRule —
    /// the creation flow that produces a RecurringTaskRoot + RecurringTaskSeries
    /// (+ optional RecurringTaskSubtask templates) and materializes the initial
    /// batch of TaskItems with their Subtasks.
    /// </summary>
    public sealed class CreateRecurringTaskEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public CreateRecurringTaskEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Create_recurring_with_count_5_creates_root_series_outbox_and_materializes_batch()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            // Anchor in the past so the materializer fully expands the COUNT=5 series
            // through "today" rather than leaving them as future virtual occurrences.
            var startDate = new DateOnly(2026, 1, 5); // a Monday
            var payload = new
            {
                Date = startDate,
                Title = "Daily standup",
                Description = "Team sync",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(9, 15),
                Priority = "Normal",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=5",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<TaskDetailDto>();
            dto.Should().NotBeNull();
            dto!.Title.Should().Be("Daily standup");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // RecurringTaskRoot
            var roots = await db.RecurringTaskRoots.AsNoTracking()
                .Where(r => r.UserId == userId).ToListAsync();
            roots.Should().HaveCount(1);
            var root = roots[0];

            // RecurringTaskSeries
            var seriesList = await db.RecurringTaskSeries.AsNoTracking()
                .Where(s => s.UserId == userId).ToListAsync();
            seriesList.Should().HaveCount(1);
            var series = seriesList[0];
            series.RootId.Should().Be(root.Id);
            series.RRuleString.Should().Be("FREQ=DAILY;COUNT=5");
            series.StartsOnDate.Should().Be(startDate);
            series.Title.Should().Be("Daily standup");

            // No template subtasks (we passed none)
            (await db.RecurringTaskSubtasks.AsNoTracking()
                .CountAsync(s => s.UserId == userId)).Should().Be(0);

            // Materialized TaskItems all linked to the series
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId).ToListAsync();
            tasks.Should().NotBeEmpty();
            tasks.Should().OnlyContain(t => t.RecurringSeriesId == series.Id);
            tasks.Should().OnlyContain(t => t.CanonicalOccurrenceDate.HasValue);
            tasks.Should().OnlyContain(t => t.Title == "Daily standup");

            // Outbox: at least one Root.Created and one Series.Created.
            var rootOutbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == root.Id
                               && o.MessageType == $"{nameof(RecurringTaskRoot)}.{RecurringRootEventType.Created}");
            rootOutbox.AggregateType.Should().Be(nameof(RecurringTaskRoot));

            var seriesOutbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == series.Id
                               && o.MessageType == $"{nameof(RecurringTaskSeries)}.{RecurringSeriesEventType.Created}");
            seriesOutbox.AggregateType.Should().Be(nameof(RecurringTaskSeries));

            // One TaskEventType.Created outbox per materialized task.
            var taskCreatedOutboxCount = await db.OutboxMessages.AsNoTracking()
                .CountAsync(o => o.UserId == userId
                              && o.AggregateType == nameof(TaskItem)
                              && o.MessageType == $"{nameof(TaskItem)}.{TaskEventType.Created}");
            taskCreatedOutboxCount.Should().Be(tasks.Count);
        }

        [Fact]
        public async Task Create_recurring_with_template_subtasks_persists_subtasks_keyed_to_series()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "Daily checklist",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=3",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = new[]
                    {
                        new { Text = "Open laptop",   Position = "a0" },
                        new { Text = "Check email",   Position = "a1" },
                        new { Text = "Review tasks",  Position = "a2" }
                    }
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var series = await db.RecurringTaskSeries.AsNoTracking()
                .SingleAsync(s => s.UserId == userId);

            var templates = await db.RecurringTaskSubtasks.AsNoTracking()
                .Where(s => s.UserId == userId).ToListAsync();
            templates.Should().HaveCount(3);
            templates.Should().OnlyContain(s => s.SeriesId == series.Id);
            templates.Should().OnlyContain(s => s.ExceptionId == null);
            templates.Select(s => s.Text).Should().BeEquivalentTo(new[] { "Open laptop", "Check email", "Review tasks" });
        }

        [Fact]
        public async Task Create_recurring_with_invalid_rrule_missing_freq_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "Bad rule",
                RecurrenceRule = new
                {
                    RRuleString = "COUNT=5", // missing FREQ=
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskRoots.CountAsync(r => r.UserId == userId)).Should().Be(0);
            (await db.RecurringTaskSeries.CountAsync(s => s.UserId == userId)).Should().Be(0);
            (await db.Tasks.CountAsync(t => t.UserId == userId)).Should().Be(0);
        }

        [Fact]
        public async Task Create_recurring_with_ends_before_not_after_starts_on_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "Bad range",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY",
                    StartsOnDate = startDate,
                    EndsBeforeDate = startDate, // not strictly after
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_recurring_with_negative_reminder_offset_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "Bad reminder",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=3",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = -5,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_recurring_with_template_subtask_empty_text_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "Daily checklist",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=3",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = new[]
                    {
                        new { Text = "",   Position = "a0" }
                    }
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_recurring_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var payload = new
            {
                Date = new DateOnly(2026, 1, 5),
                Title = "Anon",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=2",
                    StartsOnDate = new DateOnly(2026, 1, 5),
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Create_recurring_with_unknown_category_returns_4xx_and_writes_no_rows()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var startDate = new DateOnly(2026, 1, 5);
            var payload = new
            {
                Date = startDate,
                Title = "With phantom category",
                CategoryId = Guid.NewGuid(),
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=2",
                    StartsOnDate = startDate,
                    EndsBeforeDate = (DateOnly?)null,
                    ReminderOffsetMinutes = (int?)null,
                    TemplateSubtasks = (object[]?)null
                }
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);

            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskRoots.CountAsync(r => r.UserId == userId)).Should().Be(0);
            (await db.RecurringTaskSeries.CountAsync(s => s.UserId == userId)).Should().Be(0);
            (await db.Tasks.CountAsync(t => t.UserId == userId)).Should().Be(0);
        }
    }
}
