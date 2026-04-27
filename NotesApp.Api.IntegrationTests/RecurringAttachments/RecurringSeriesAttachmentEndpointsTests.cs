using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.RecurringAttachments
{
    /// <summary>
    /// End-to-end HTTP tests for /api/recurring-attachments/series/* exercising
    /// validator, handler, fake blob storage, persistence, and outbox emission.
    /// </summary>
    public sealed class RecurringSeriesAttachmentEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public RecurringSeriesAttachmentEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------
        // POST /api/recurring-attachments/series/{seriesId} (multipart)
        // -----------------------------------------------------------------

        [Fact]
        public async Task Upload_to_series_valid_returns_201_persists_row_writes_blob_and_emits_outbox_created()
        {
            var (userId, seriesId, client) = await SeedFutureSeriesAsync();

            var response = await UploadToSeriesAsync(client, seriesId, "spec.pdf", "application/pdf", "bytes"u8.ToArray());

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>();
            dto.Should().NotBeNull();
            dto!.AttachmentId.Should().NotBeEmpty();
            dto.SeriesId.Should().Be(seriesId);
            dto.ExceptionId.Should().BeNull();
            dto.DisplayOrder.Should().Be(1);
            dto.DownloadUrl.Should().Contain("fake-storage.test");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.RecurringTaskAttachments.AsNoTracking()
                .SingleAsync(a => a.Id == dto.AttachmentId);
            row.UserId.Should().Be(userId);
            row.SeriesId.Should().Be(seriesId);
            row.ExceptionId.Should().BeNull();
            row.FileName.Should().Be("spec.pdf");
            row.ContentType.Should().Be("application/pdf");
            row.DisplayOrder.Should().Be(1);
            row.IsDeleted.Should().BeFalse();
            row.BlobPath.Should().NotBeNullOrWhiteSpace();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == dto.AttachmentId && o.UserId == userId);
            outbox.AggregateType.Should().Be(nameof(RecurringTaskAttachment));
            outbox.MessageType.Should().Be($"{nameof(RecurringTaskAttachment)}.{RecurringAttachmentEventType.Created}");
            outbox.Payload.Should().NotBeNullOrWhiteSpace();
            outbox.ProcessedAtUtc.Should().BeNull();
        }

        [Fact]
        public async Task Upload_second_series_attachment_assigns_display_order_2()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();

            (await UploadToSeriesAsync(client, seriesId, "a.pdf", "application/pdf", new byte[] { 1, 2 }))
                .EnsureSuccessStatusCode();

            var response = await UploadToSeriesAsync(client, seriesId, "b.pdf", "application/pdf", new byte[] { 3, 4 });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>();
            dto!.DisplayOrder.Should().Be(2);
        }

        [Fact]
        public async Task Upload_to_other_users_series_fails_and_writes_no_row()
        {
            var (_, seriesId, _) = await SeedFutureSeriesAsync();
            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());

            var response = await UploadToSeriesAsync(attackerClient, seriesId, "x.pdf", "application/pdf", new byte[] { 1 });

            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskAttachments.CountAsync(a => a.SeriesId == seriesId)).Should().Be(0);
        }

        [Fact]
        public async Task Upload_with_disallowed_content_type_returns_4xx_and_writes_no_row()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();

            var response = await UploadToSeriesAsync(client, seriesId, "evil.exe", "application/x-msdownload", new byte[] { 9 });

            response.IsSuccessStatusCode.Should().BeFalse();
            ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400).And.BeLessThan(500);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskAttachments.CountAsync(a => a.SeriesId == seriesId)).Should().Be(0);
        }

        [Fact]
        public async Task Upload_to_series_without_auth_returns_401()
        {
            var anon = _factory.CreateClient();
            using var content = BuildMultipart("a.pdf", "application/pdf", new byte[] { 1 });

            var response = await anon.PostAsync($"/api/recurring-attachments/series/{Guid.NewGuid()}", content);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // -----------------------------------------------------------------
        // DELETE /api/recurring-attachments/series/{id}
        // -----------------------------------------------------------------

        [Fact]
        public async Task Delete_series_attachment_returns_204_soft_deletes_and_emits_outbox_deleted()
        {
            var (userId, seriesId, client) = await SeedFutureSeriesAsync();
            var (attachmentId, rowVersion) = await UploadAndReturnIdAsync(client, seriesId);

            var response = await client.DeleteAsJsonAsync(
                $"/api/recurring-attachments/series/{attachmentId}",
                new { RowVersion = rowVersion });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var row = await db.RecurringTaskAttachments.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(a => a.Id == attachmentId);
            row.IsDeleted.Should().BeTrue();

            var outbox = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(o => o.AggregateId == attachmentId
                                  && o.MessageType == $"{nameof(RecurringTaskAttachment)}.{RecurringAttachmentEventType.Deleted}"
                                  && o.UserId == userId);
            outbox.AggregateType.Should().Be(nameof(RecurringTaskAttachment));
        }

        [Fact]
        public async Task Delete_series_attachment_by_wrong_user_fails_and_does_not_soft_delete()
        {
            var (_, seriesId, ownerClient) = await SeedFutureSeriesAsync();
            var (attachmentId, rowVersion) = await UploadAndReturnIdAsync(ownerClient, seriesId);

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var response = await attackerClient.DeleteAsJsonAsync(
                $"/api/recurring-attachments/series/{attachmentId}",
                new { RowVersion = rowVersion });

            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.RecurringTaskAttachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId);
            row.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task Get_series_attachment_download_url_returns_200_with_url()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();
            var (attachmentId, _) = await UploadAndReturnIdAsync(client, seriesId);

            var response = await client.GetAsync($"/api/recurring-attachments/series/{attachmentId}/download-url");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("fake-storage.test");
        }

        [Fact]
        public async Task Get_series_attachment_download_url_for_other_users_attachment_fails()
        {
            var (_, seriesId, ownerClient) = await SeedFutureSeriesAsync();
            var (attachmentId, _) = await UploadAndReturnIdAsync(ownerClient, seriesId);

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var response = await attackerClient.GetAsync($"/api/recurring-attachments/series/{attachmentId}/download-url");

            response.IsSuccessStatusCode.Should().BeFalse();
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private async Task<(Guid userId, Guid seriesId, HttpClient client)> SeedFutureSeriesAsync()
        {
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var startDate = new DateOnly(2027, 1, 4);
            var payload = new
            {
                Date = startDate,
                Title = "Series for attachment",
                RecurrenceRule = new
                {
                    RRuleString = "FREQ=DAILY;COUNT=5",
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

            // userId is the external claim (oid); look up the internal DB user via UserLogins.
            var login = await db.UserLogins.AsNoTracking()
                .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
            var seriesId = (await db.RecurringTaskSeries.AsNoTracking()
                .SingleAsync(s => s.UserId == login.UserId)).Id;
            return (userId, seriesId, client);
        }

        private async Task<(Guid AttachmentId, byte[] RowVersion)> UploadAndReturnIdAsync(HttpClient client, Guid seriesId)
        {
            var resp = await UploadToSeriesAsync(client, seriesId, "seed.pdf", "application/pdf", new byte[] { 1, 2, 3 });
            resp.EnsureSuccessStatusCode();
            var dto = (await resp.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.RecurringTaskAttachments.AsNoTracking().SingleAsync(a => a.Id == dto.AttachmentId);
            return (dto.AttachmentId, row.RowVersion);
        }

        private static MultipartFormDataContent BuildMultipart(string fileName, string contentType, byte[] bytes)
        {
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);
            return content;
        }

        private static Task<HttpResponseMessage> UploadToSeriesAsync(
            HttpClient client, Guid seriesId, string fileName, string contentType, byte[] bytes)
        {
            var content = BuildMultipart(fileName, contentType, bytes);
            return client.PostAsync($"/api/recurring-attachments/series/{seriesId}", content);
        }
    }
}
