using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.Categories.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Categories
{
    /// <summary>
    /// End-to-end HTTP tests for /api/categories exercising validator,
    /// handler, domain guards, persistence, and outbox emission.
    ///
    /// Isolation strategy: each test uses a fresh random userId and filters
    /// DB queries by userId. The DB is shared but queries never cross users.
    /// </summary>
    public sealed class CategoriesEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public CategoriesEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------------
        // POST /api/categories
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Create_with_valid_payload_returns_201_persists_row_and_emits_outbox_created()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var response = await client.PostAsJsonAsync("/api/categories", new { Name = "Work" });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<TaskCategoryDto>();
            dto.Should().NotBeNull();
            dto!.CategoryId.Should().NotBeEmpty();
            dto.Name.Should().Be("Work");
            dto.Version.Should().Be(1);
            dto.RowVersion.Should().NotBeEmpty();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.TaskCategories
                .AsNoTracking()
                .SingleAsync(c => c.Id == dto.CategoryId);

            // userId is the external claim (oid); CurrentUserService stores an auto-generated
            // internal Id. Derive the real internal userId from the row we just fetched.
            var internalUserId = row.UserId;
            internalUserId.Should().NotBeEmpty();
            row.Name.Should().Be("Work");
            row.IsDeleted.Should().BeFalse();
            row.Version.Should().Be(1);

            var outbox = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(o => o.AggregateId == dto.CategoryId && o.UserId == internalUserId);

            outbox.AggregateType.Should().Be(nameof(TaskCategory));
            outbox.MessageType.Should().Be($"{nameof(TaskCategory)}.{TaskCategoryEventType.Created}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
        }

        [Fact]
        public async Task Create_with_empty_name_returns_400_and_writes_no_rows()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var response = await client.PostAsJsonAsync("/api/categories", new { Name = "" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            (await db.TaskCategories.CountAsync(c => c.UserId == userId)).Should().Be(0);
            (await db.OutboxMessages.CountAsync(o => o.UserId == userId)).Should().Be(0);
        }

        [Fact]
        public async Task Create_with_name_exceeding_max_length_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var name = new string('a', TaskCategory.MaxNameLength + 1);
            var response = await client.PostAsJsonAsync("/api/categories", new { Name = name });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Create_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/categories", new { Name = "Work" });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // -----------------------------------------------------------------------
        // PUT /api/categories/{id}
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Update_rename_returns_200_increments_version_and_emits_outbox_updated()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var created = await CreateCategoryAsync(client, "Work");

            var response = await client.PutAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { Name = "Work-Renamed", RowVersion = created.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await response.Content.ReadFromJsonAsync<TaskCategoryDto>();
            updated!.Name.Should().Be("Work-Renamed");
            updated.Version.Should().Be(created.Version + 1);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.TaskCategories.AsNoTracking()
                .SingleAsync(c => c.Id == created.CategoryId);
            row.Name.Should().Be("Work-Renamed");
            row.Version.Should().Be(created.Version + 1);

            var updatedOutbox = await db.OutboxMessages.AsNoTracking()
                .Where(o => o.AggregateId == created.CategoryId
                         && o.MessageType == $"{nameof(TaskCategory)}.{TaskCategoryEventType.Updated}")
                .ToListAsync();

            updatedOutbox.Should().HaveCount(1);
            updatedOutbox[0].AggregateType.Should().Be(nameof(TaskCategory));
            updatedOutbox[0].Payload.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Update_by_wrong_user_returns_404_and_does_not_touch_row()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var created = await CreateCategoryAsync(ownerClient, "Work");

            var response = await otherClient.PutAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { Name = "Hijacked", RowVersion = created.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.TaskCategories.AsNoTracking()
                .SingleAsync(c => c.Id == created.CategoryId);
            row.Name.Should().Be("Work");
            row.Version.Should().Be(created.Version);

            (await db.OutboxMessages.CountAsync(
                o => o.AggregateId == created.CategoryId
                  && o.MessageType == $"{nameof(TaskCategory)}.{TaskCategoryEventType.Updated}"))
                .Should().Be(0);
        }

        [Fact]
        public async Task Update_with_stale_rowversion_returns_409()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var created = await CreateCategoryAsync(client, "Work");

            // First rename succeeds, second attempt with original RowVersion is stale.
            var first = await client.PutAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { Name = "First", RowVersion = created.RowVersion });
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            var stale = await client.PutAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { Name = "Stale", RowVersion = created.RowVersion });

            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Update_non_existent_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var response = await client.PutAsJsonAsync(
                $"/api/categories/{Guid.NewGuid()}",
                new { Name = "Anything", RowVersion = HttpClientExtensions.PlaceholderRowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Update_with_empty_name_returns_400()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var created = await CreateCategoryAsync(client, "Work");

            var response = await client.PutAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { Name = "", RowVersion = created.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // -----------------------------------------------------------------------
        // GET /api/categories
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Get_list_returns_only_callers_non_deleted_categories()
        {
            var userId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var mine1 = await CreateCategoryAsync(client, "Work");
            var mine2 = await CreateCategoryAsync(client, "Personal");
            var theirs = await CreateCategoryAsync(otherClient, "Their-Work");

            var response = await client.GetAsync("/api/categories");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var list = await response.Content.ReadFromJsonAsync<List<TaskCategoryDto>>();
            list.Should().NotBeNull();
            list!.Select(c => c.CategoryId).Should().BeEquivalentTo(new[] { mine1.CategoryId, mine2.CategoryId });
            list.Select(c => c.CategoryId).Should().NotContain(theirs.CategoryId);
        }

        [Fact]
        public async Task Get_detail_for_own_category_returns_200()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var created = await CreateCategoryAsync(client, "Work");

            var response = await client.GetAsync($"/api/categories/{created.CategoryId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await response.Content.ReadFromJsonAsync<TaskCategoryDto>();
            dto!.CategoryId.Should().Be(created.CategoryId);
            dto.Name.Should().Be("Work");
        }

        [Fact]
        public async Task Get_detail_for_other_users_category_returns_404()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var created = await CreateCategoryAsync(ownerClient, "Work");

            var response = await otherClient.GetAsync($"/api/categories/{created.CategoryId}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------------
        // DELETE /api/categories/{id}
        //
        // Deep deletion semantics (FK clearing, isolation, idempotency) are already
        // covered by DeleteTaskCategoryIntegrationTests at the handler level. This
        // test only verifies the HTTP path: controller wiring, 204 response, outbox
        // emission on the happy path.
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Delete_own_category_returns_204_soft_deletes_and_emits_outbox_deleted()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var created = await CreateCategoryAsync(client, "Work");

            var response = await client.DeleteAsJsonAsync(
                $"/api/categories/{created.CategoryId}",
                new { RowVersion = created.RowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.TaskCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(c => c.Id == created.CategoryId);
            row.IsDeleted.Should().BeTrue();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == created.CategoryId
                               && o.MessageType == $"{nameof(TaskCategory)}.{TaskCategoryEventType.Deleted}");
            outbox.AggregateType.Should().Be(nameof(TaskCategory));
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static async Task<TaskCategoryDto> CreateCategoryAsync(HttpClient client, string name)
        {
            var response = await client.PostAsJsonAsync("/api/categories", new { Name = name });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TaskCategoryDto>())!;
        }
    }
}
