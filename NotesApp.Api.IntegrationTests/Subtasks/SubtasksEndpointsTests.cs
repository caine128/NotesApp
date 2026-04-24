using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.Subtasks.Models;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Subtasks
{
    /// <summary>
    /// End-to-end HTTP tests for /api/tasks/{taskId}/subtasks exercising
    /// validator, handler, domain guards, persistence, and outbox emission.
    ///
    /// Isolation strategy: each test uses a fresh random userId and filters
    /// DB queries by userId. The DB is shared but queries never cross users.
    /// </summary>
    public sealed class SubtasksEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public SubtasksEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------------
        // POST /api/tasks/{taskId}/subtasks
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Create_with_valid_payload_returns_201_persists_row_and_emits_outbox_created()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var task = await CreateTaskAsync(client);

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Buy milk", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<SubtaskDto>();
            dto.Should().NotBeNull();
            dto!.SubtaskId.Should().NotBeEmpty();
            dto.Text.Should().Be("Buy milk");
            dto.Position.Should().Be("a0");
            dto.IsCompleted.Should().BeFalse();
            dto.Version.Should().Be(1);
            dto.RowVersion.Should().NotBeEmpty();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Subtasks.AsNoTracking()
                .SingleAsync(s => s.Id == dto.SubtaskId);
            row.UserId.Should().Be(userId);
            row.TaskId.Should().Be(task.TaskId);
            row.Text.Should().Be("Buy milk");
            row.Position.Should().Be("a0");
            row.IsCompleted.Should().BeFalse();
            row.IsDeleted.Should().BeFalse();
            row.Version.Should().Be(1);

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == dto.SubtaskId && o.UserId == userId);
            outbox.AggregateType.Should().Be(nameof(Subtask));
            outbox.MessageType.Should().Be($"{nameof(Subtask)}.{SubtaskEventType.Created}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
        }

        [Fact]
        public async Task Create_with_empty_text_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Subtasks.CountAsync(s => s.TaskId == task.TaskId)).Should().Be(0);
        }

        [Fact]
        public async Task Create_with_text_exceeding_max_length_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var text = new string('a', Subtask.MaxTextLength + 1);
            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = text, Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_with_empty_position_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Hello", Position = "" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_with_position_exceeding_max_length_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var position = new string('a', Subtask.MaxPositionLength + 1);
            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Hello", Position = position });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_on_non_existent_parent_task_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{Guid.NewGuid()}/subtasks",
                new { Text = "Hello", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Create_on_other_users_task_returns_404_and_writes_nothing()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var task = await CreateTaskAsync(ownerClient);

            var response = await otherClient.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Hijacked", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Subtasks.CountAsync(s => s.TaskId == task.TaskId)).Should().Be(0);
        }

        [Fact]
        public async Task Create_on_soft_deleted_parent_task_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var deleteResp = await client.DeleteAsJsonAsync(
                $"/api/tasks/{task.TaskId}",
                new { RowVersion = task.RowVersion });
            deleteResp.EnsureSuccessStatusCode();

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Too late", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Create_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{Guid.NewGuid()}/subtasks",
                new { Text = "Hi", Position = "a0" });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // -----------------------------------------------------------------------
        // PUT /api/tasks/{taskId}/subtasks/{subtaskId}
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Update_text_only_returns_200_bumps_version_and_emits_outbox_updated()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Text = "Buy cheese", RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await response.Content.ReadFromJsonAsync<SubtaskDto>();
            updated!.Text.Should().Be("Buy cheese");
            updated.Version.Should().Be(subtask.Version + 1);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Subtasks.AsNoTracking()
                .SingleAsync(s => s.Id == subtask.SubtaskId);
            row.Text.Should().Be("Buy cheese");
            row.Version.Should().Be(subtask.Version + 1);

            var updatedOutbox = await db.OutboxMessages.AsNoTracking()
                .Where(o => o.AggregateId == subtask.SubtaskId
                         && o.MessageType == $"{nameof(Subtask)}.{SubtaskEventType.Updated}")
                .ToListAsync();
            updatedOutbox.Should().HaveCount(1);
        }

        [Fact]
        public async Task Update_is_completed_true_bumps_version_and_emits_outbox_updated()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { IsCompleted = true, RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await response.Content.ReadFromJsonAsync<SubtaskDto>();
            updated!.IsCompleted.Should().BeTrue();
            updated.Version.Should().Be(subtask.Version + 1);
        }

        /// <summary>
        /// Setting IsCompleted to the current value is idempotent at the domain layer —
        /// no Version bump. Documents the current behavior: row stays at same version
        /// and UpdatedAtUtc is unchanged.
        /// </summary>
        [Fact]
        public async Task Update_is_completed_to_same_value_is_idempotent_no_version_bump()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { IsCompleted = false, RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Subtasks.AsNoTracking()
                .SingleAsync(s => s.Id == subtask.SubtaskId);
            row.Version.Should().Be(subtask.Version);
            row.UpdatedAtUtc.Should().Be(subtask.UpdatedAtUtc);
        }

        [Fact]
        public async Task Update_position_returns_200_and_bumps_version()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Position = "a1", RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await response.Content.ReadFromJsonAsync<SubtaskDto>();
            updated!.Position.Should().Be("a1");
            updated.Version.Should().Be(subtask.Version + 1);
        }

        [Fact]
        public async Task Update_by_wrong_user_returns_404_and_does_not_touch_row()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var (task, subtask) = await CreateTaskAndSubtaskAsync(ownerClient);

            var response = await otherClient.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Text = "Hijacked", RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Subtasks.AsNoTracking()
                .SingleAsync(s => s.Id == subtask.SubtaskId);
            row.Text.Should().Be(subtask.Text);
            row.Version.Should().Be(subtask.Version);
        }

        [Fact]
        public async Task Update_with_taskid_mismatch_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);
            var otherTask = await CreateTaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{otherTask.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Text = "Wrong parent", RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Update_with_stale_rowversion_returns_409()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var first = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Text = "First", RowVersion = subtask.RowVersion });
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            var stale = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { Text = "Stale", RowVersion = subtask.RowVersion });

            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Update_non_existent_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await client.PutAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{Guid.NewGuid()}",
                new { Text = "Nope", RowVersion = HttpClientExtensions.PlaceholderRowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------------
        // DELETE /api/tasks/{taskId}/subtasks/{subtaskId}
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Delete_own_subtask_returns_204_soft_deletes_and_emits_outbox_deleted()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, subtask) = await CreateTaskAndSubtaskAsync(client);

            var response = await client.DeleteAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Subtasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(s => s.Id == subtask.SubtaskId);
            row.IsDeleted.Should().BeTrue();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == subtask.SubtaskId
                               && o.MessageType == $"{nameof(Subtask)}.{SubtaskEventType.Deleted}");
            outbox.AggregateType.Should().Be(nameof(Subtask));
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Delete_by_wrong_user_returns_404_and_does_not_soft_delete()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var (task, subtask) = await CreateTaskAndSubtaskAsync(ownerClient);

            var response = await otherClient.DeleteAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{subtask.SubtaskId}",
                new { RowVersion = subtask.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Subtasks.AsNoTracking()
                .SingleAsync(s => s.Id == subtask.SubtaskId);
            row.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_non_existent_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await client.DeleteAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks/{Guid.NewGuid()}",
                new { RowVersion = HttpClientExtensions.PlaceholderRowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static async Task<TaskDetailDto> CreateTaskAsync(HttpClient client)
        {
            var payload = new
            {
                Date = new DateOnly(2026, 4, 24),
                Title = "Parent task"
            };

            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TaskDetailDto>())!;
        }

        private static async Task<(TaskDetailDto Task, SubtaskDto Subtask)> CreateTaskAndSubtaskAsync(HttpClient client)
        {
            var task = await CreateTaskAsync(client);
            var response = await client.PostAsJsonAsync(
                $"/api/tasks/{task.TaskId}/subtasks",
                new { Text = "Initial", Position = "a0" });
            response.EnsureSuccessStatusCode();
            var subtask = (await response.Content.ReadFromJsonAsync<SubtaskDto>())!;
            return (task, subtask);
        }
    }
}
