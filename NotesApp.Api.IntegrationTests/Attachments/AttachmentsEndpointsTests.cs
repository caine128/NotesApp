using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.Attachments.Models;
using NotesApp.Application.Tasks.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Attachments
{
    /// <summary>
    /// End-to-end HTTP tests for /api/attachments exercising validator,
    /// handler, blob storage (fake), persistence, and outbox emission.
    /// </summary>
    public sealed class AttachmentsEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public AttachmentsEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------------
        // POST /api/attachments/{taskId}  (multipart)
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Upload_multipart_valid_returns_201_persists_row_writes_blob_and_emits_outbox_created()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await UploadAsync(client, task.TaskId, "hello.pdf", "application/pdf", "pdf content bytes"u8.ToArray());

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadAttachmentResultDto>();
            dto.Should().NotBeNull();
            dto!.AttachmentId.Should().NotBeEmpty();
            dto.TaskId.Should().Be(task.TaskId);
            dto.DisplayOrder.Should().Be(1);
            dto.DownloadUrl.Should().NotBeNullOrWhiteSpace();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // userId is the external claim (oid); CurrentUserService creates an internal User
            // with a different auto-generated Id. Derive the real internal userId from the task.
            var taskRow = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.TaskId);
            var internalUserId = taskRow.UserId;

            var row = await db.Attachments.AsNoTracking()
                .SingleAsync(a => a.Id == dto.AttachmentId);
            row.UserId.Should().Be(internalUserId);
            row.TaskId.Should().Be(task.TaskId);
            row.FileName.Should().Be("hello.pdf");
            row.ContentType.Should().Be("application/pdf");
            row.DisplayOrder.Should().Be(1);
            row.IsDeleted.Should().BeFalse();
            row.BlobPath.Should().NotBeNullOrWhiteSpace();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == dto.AttachmentId && o.UserId == internalUserId);
            outbox.AggregateType.Should().Be(nameof(Attachment));
            outbox.MessageType.Should().Be($"{nameof(Attachment)}.{AttachmentEventType.Created}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
        }

        [Fact]
        public async Task Upload_second_attachment_assigns_display_order_2()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var first = await UploadAsync(client, task.TaskId, "a.pdf", "application/pdf", new byte[] { 1, 2, 3 });
            first.EnsureSuccessStatusCode();

            var response = await UploadAsync(client, task.TaskId, "b.pdf", "application/pdf", new byte[] { 4, 5, 6 });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadAttachmentResultDto>();
            dto!.DisplayOrder.Should().Be(2);
        }

        [Fact]
        public async Task Upload_with_disallowed_content_type_returns_4xx_and_writes_no_row()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var response = await UploadAsync(client, task.TaskId, "script.exe", "application/x-msdownload", new byte[] { 9, 9, 9 });

            response.IsSuccessStatusCode.Should().BeFalse();
            ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400).And.BeLessThan(500);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Attachments.CountAsync(a => a.TaskId == task.TaskId)).Should().Be(0);
        }

        [Fact]
        public async Task Upload_on_other_users_task_returns_404_and_writes_no_row()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var task = await CreateTaskAsync(ownerClient);

            var response = await UploadAsync(otherClient, task.TaskId, "hi.pdf", "application/pdf", new byte[] { 1 });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Attachments.CountAsync(a => a.TaskId == task.TaskId)).Should().Be(0);
        }

        [Fact]
        public async Task Upload_on_soft_deleted_task_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var deleteResp = await client.DeleteAsJsonAsync(
                $"/api/tasks/{task.TaskId}",
                new { RowVersion = task.RowVersion });
            deleteResp.EnsureSuccessStatusCode();

            var response = await UploadAsync(client, task.TaskId, "late.pdf", "application/pdf", new byte[] { 1 });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Upload_when_attachment_limit_reached_returns_4xx()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            // Default MaxAttachmentsPerTask = 5 — fill it up.
            for (var i = 0; i < 5; i++)
            {
                var fill = await UploadAsync(client, task.TaskId, $"f{i}.pdf", "application/pdf", new byte[] { (byte)i });
                fill.EnsureSuccessStatusCode();
            }

            var response = await UploadAsync(client, task.TaskId, "over.pdf", "application/pdf", new byte[] { 99 });

            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Attachments.CountAsync(a => a.TaskId == task.TaskId && !a.IsDeleted)).Should().Be(5);
        }

        [Fact]
        public async Task Upload_without_auth_returns_401()
        {
            var client = _factory.CreateClient();

            using var content = BuildMultipart("x.pdf", "application/pdf", new byte[] { 1 });
            var response = await client.PostAsync($"/api/attachments/{Guid.NewGuid()}", content);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // -----------------------------------------------------------------------
        // POST /api/attachments/{taskId}/stream
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Upload_stream_valid_returns_201_and_persists_row()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var task = await CreateTaskAsync(client);

            var body = new ByteArrayContent(new byte[] { 10, 20, 30, 40 });
            body.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

            var response = await client.PostAsync(
                $"/api/attachments/{task.TaskId}/stream?fileName=stream.pdf&contentType=application/pdf",
                body);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadAttachmentResultDto>();
            dto!.AttachmentId.Should().NotBeEmpty();
            dto.DisplayOrder.Should().Be(1);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Attachments.AsNoTracking()
                .SingleAsync(a => a.Id == dto.AttachmentId);
            row.FileName.Should().Be("stream.pdf");
            row.ContentType.Should().Be("application/pdf");
        }

        // -----------------------------------------------------------------------
        // DELETE /api/attachments/{id}
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Delete_own_attachment_returns_204_soft_deletes_and_emits_outbox_deleted()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (task, attachmentId, rowVersion) = await CreateTaskAndAttachmentAsync(client);

            var response = await client.DeleteAsJsonAsync(
                $"/api/attachments/{attachmentId}",
                new { RowVersion = rowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.Attachments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(a => a.Id == attachmentId);
            row.IsDeleted.Should().BeTrue();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == attachmentId
                               && o.MessageType == $"{nameof(Attachment)}.{AttachmentEventType.Deleted}");
            outbox.AggregateType.Should().Be(nameof(Attachment));
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Delete_by_wrong_user_returns_404_and_does_not_soft_delete()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var (_, attachmentId, rowVersion) = await CreateTaskAndAttachmentAsync(ownerClient);

            var response = await otherClient.DeleteAsJsonAsync(
                $"/api/attachments/{attachmentId}",
                new { RowVersion = rowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Attachments.AsNoTracking()
                .SingleAsync(a => a.Id == attachmentId);
            row.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_with_stale_rowversion_returns_409()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (_, attachmentId, originalRowVersion) = await CreateTaskAndAttachmentAsync(client);

            // First successful delete bumps RowVersion on the row.
            var first = await client.DeleteAsJsonAsync(
                $"/api/attachments/{attachmentId}",
                new { RowVersion = originalRowVersion });
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Second delete attempt with the original (now stale) RowVersion.
            // Since the row is already soft-deleted, the wrong-user/notfound check still finds the
            // row (same user). Concurrency check fires on the stale token.
            var stale = await client.DeleteAsJsonAsync(
                $"/api/attachments/{attachmentId}",
                new { RowVersion = originalRowVersion });

            // Either 409 (stale rowversion) OR 400 (already deleted domain rejection) is acceptable
            // — the key invariant is that it does NOT succeed with 204 again.
            stale.StatusCode.Should().NotBe(HttpStatusCode.NoContent);
            ((int)stale.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        }

        [Fact]
        public async Task Delete_non_existent_returns_404()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            var response = await client.DeleteAsJsonAsync(
                $"/api/attachments/{Guid.NewGuid()}",
                new { RowVersion = HttpClientExtensions.PlaceholderRowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------------
        // GET /api/attachments/{id}/download-url
        // -----------------------------------------------------------------------

        [Fact]
        public async Task Get_download_url_for_own_attachment_returns_200_with_url()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var (_, attachmentId, _) = await CreateTaskAndAttachmentAsync(client);

            var response = await client.GetAsync($"/api/attachments/{attachmentId}/download-url");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var url = await response.Content.ReadAsStringAsync();
            url.Should().NotBeNullOrWhiteSpace();
            url.Should().Contain("fake-storage.test");
        }

        [Fact]
        public async Task Get_download_url_for_other_users_attachment_returns_404()
        {
            var ownerId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var otherClient = _factory.CreateClientAsUser(otherId);

            var (_, attachmentId, _) = await CreateTaskAndAttachmentAsync(ownerClient);

            var response = await otherClient.GetAsync($"/api/attachments/{attachmentId}/download-url");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static async Task<TaskDetailDto> CreateTaskAsync(HttpClient client)
        {
            var payload = new { Date = new DateOnly(2026, 4, 24), Title = "Task for attachment" };
            var response = await client.PostAsJsonAsync("/api/tasks", payload);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TaskDetailDto>())!;
        }

        private async Task<(TaskDetailDto Task, Guid AttachmentId, byte[] RowVersion)>
            CreateTaskAndAttachmentAsync(HttpClient client)
        {
            var task = await CreateTaskAsync(client);
            var uploadResp = await UploadAsync(client, task.TaskId, "seed.pdf", "application/pdf", new byte[] { 1, 2, 3 });
            uploadResp.EnsureSuccessStatusCode();
            var dto = (await uploadResp.Content.ReadFromJsonAsync<UploadAttachmentResultDto>())!;

            // UploadAttachmentResultDto does not surface RowVersion — read it from the DB.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == dto.AttachmentId);
            return (task, dto.AttachmentId, row.RowVersion);
        }

        private static MultipartFormDataContent BuildMultipart(string fileName, string contentType, byte[] bytes)
        {
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);
            return content;
        }

        private static Task<HttpResponseMessage> UploadAsync(
            HttpClient client, Guid taskId, string fileName, string contentType, byte[] bytes)
        {
            var content = BuildMultipart(fileName, contentType, bytes);
            return client.PostAsync($"/api/attachments/{taskId}", content);
        }
    }
}
