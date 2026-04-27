using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Api.IntegrationTests.Infrastructure.Http;
using NotesApp.Application.RecurringAttachments.Models;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.RecurringAttachments
{
    /// <summary>
    /// End-to-end HTTP tests for /api/recurring-attachments/occurrences/* exercising
    /// first-touch exception promotion, template copy, and HasAttachmentOverride invariant.
    /// </summary>
    public sealed class RecurringOccurrenceAttachmentEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public RecurringOccurrenceAttachmentEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        // -----------------------------------------------------------------
        // POST /api/recurring-attachments/occurrences/{seriesId}/{date}
        // -----------------------------------------------------------------

        [Fact]
        public async Task Upload_to_virtual_occurrence_creates_exception_and_marks_attachment_override()
        {
            var (userId, seriesId, client) = await SeedFutureSeriesAsync();
            var occurrenceDate = new DateOnly(2027, 1, 8);

            var response = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "page.pdf", "application/pdf", new byte[] { 1, 2 });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await response.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>();
            dto.Should().NotBeNull();
            dto!.SeriesId.Should().BeNull();
            dto.ExceptionId.Should().NotBeNull();
            dto.DisplayOrder.Should().Be(1);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate);
            ex.Id.Should().Be(dto.ExceptionId!.Value);
            ex.IsDeletion.Should().BeFalse();
            ex.HasAttachmentOverride.Should().BeTrue();

            var attachment = await db.RecurringTaskAttachments.AsNoTracking()
                .SingleAsync(a => a.Id == dto.AttachmentId);
            attachment.UserId.Should().Be(userId);
            attachment.SeriesId.Should().BeNull();
            attachment.ExceptionId.Should().Be(ex.Id);
        }

        [Fact]
        public async Task Upload_to_occurrence_after_series_attachment_copies_template_and_links_exception()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();

            // Seed a series template attachment first.
            var seriesUpload = await UploadSeriesAttachmentAsync(client, seriesId, "template.pdf");
            var templateAttachmentId = seriesUpload.AttachmentId;

            var occurrenceDate = new DateOnly(2027, 1, 7);
            var response = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "extra.pdf", "application/pdf", new byte[] { 9 });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = (await response.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Two attachments now linked to the new exception: the copy + the new upload.
            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate);
            ex.HasAttachmentOverride.Should().BeTrue();

            var exceptionAttachments = await db.RecurringTaskAttachments.AsNoTracking()
                .Where(a => a.ExceptionId == ex.Id).OrderBy(a => a.DisplayOrder).ToListAsync();
            exceptionAttachments.Should().HaveCount(2);

            // Copy carries the same BlobPath as the template (orphan-cleanup invariant).
            var template = await db.RecurringTaskAttachments.AsNoTracking()
                .SingleAsync(a => a.Id == templateAttachmentId);
            exceptionAttachments.Should().Contain(a => a.BlobPath == template.BlobPath);
            exceptionAttachments.Should().Contain(a => a.Id == dto.AttachmentId);

            // Original series template attachment is left intact (only the exception copy is new).
            template.IsDeleted.Should().BeFalse();
            template.SeriesId.Should().Be(seriesId);
        }

        [Fact]
        public async Task Second_upload_to_same_occurrence_assigns_display_order_2_and_does_not_recreate_exception()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();
            var occurrenceDate = new DateOnly(2027, 1, 6);

            var first = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "a.pdf", "application/pdf", new byte[] { 1 });
            first.EnsureSuccessStatusCode();
            var firstDto = (await first.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            var second = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "b.pdf", "application/pdf", new byte[] { 2 });
            second.StatusCode.Should().Be(HttpStatusCode.Created);
            var secondDto = (await second.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            secondDto.ExceptionId.Should().Be(firstDto.ExceptionId);
            secondDto.DisplayOrder.Should().Be(2);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskExceptions.AsNoTracking()
                .CountAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate))
                .Should().Be(1);
        }

        [Fact]
        public async Task Delete_all_occurrence_attachments_keeps_HasAttachmentOverride_true()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();
            var occurrenceDate = new DateOnly(2027, 1, 5);

            var resp = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "only.pdf", "application/pdf", new byte[] { 7 });
            resp.EnsureSuccessStatusCode();
            var dto = (await resp.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            using (var scope0 = _factory.Services.CreateScope())
            {
                var db0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
                var rowVersion = (await db0.RecurringTaskAttachments.AsNoTracking()
                    .SingleAsync(a => a.Id == dto.AttachmentId)).RowVersion;

                var del = await client.DeleteAsJsonAsync(
                    $"/api/recurring-attachments/occurrences/{seriesId}/{occurrenceDate:yyyy-MM-dd}/{dto.AttachmentId}",
                    new { RowVersion = rowVersion });
                del.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var attachment = await db.RecurringTaskAttachments.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(a => a.Id == dto.AttachmentId);
            attachment.IsDeleted.Should().BeTrue();

            // Exception remains with HasAttachmentOverride still true (prevents snap-back to series template).
            var ex = await db.RecurringTaskExceptions.AsNoTracking()
                .SingleAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate);
            ex.HasAttachmentOverride.Should().BeTrue();
        }

        [Fact]
        public async Task Upload_to_other_users_occurrence_fails_and_writes_no_exception()
        {
            var (_, seriesId, _) = await SeedFutureSeriesAsync();
            var occurrenceDate = new DateOnly(2027, 1, 8);

            var attackerClient = _factory.CreateClientAsUser(Guid.NewGuid());
            var response = await UploadToOccurrenceAsync(
                attackerClient, seriesId, occurrenceDate, "x.pdf", "application/pdf", new byte[] { 1 });

            response.IsSuccessStatusCode.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RecurringTaskExceptions.AsNoTracking()
                .CountAsync(e => e.SeriesId == seriesId && e.OccurrenceDate == occurrenceDate))
                .Should().Be(0);
            (await db.RecurringTaskAttachments.AsNoTracking()
                .CountAsync(a => a.SeriesId == seriesId)).Should().Be(0);
        }

        [Fact]
        public async Task Upload_to_occurrence_without_auth_returns_401()
        {
            var anon = _factory.CreateClient();
            using var content = BuildMultipart("a.pdf", "application/pdf", new byte[] { 1 });

            var response = await anon.PostAsync(
                $"/api/recurring-attachments/occurrences/{Guid.NewGuid()}/2027-01-08", content);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Get_occurrence_download_url_for_own_attachment_returns_200_with_url()
        {
            var (_, seriesId, client) = await SeedFutureSeriesAsync();
            var occurrenceDate = new DateOnly(2027, 1, 9);

            var resp = await UploadToOccurrenceAsync(
                client, seriesId, occurrenceDate, "f.pdf", "application/pdf", new byte[] { 1 });
            resp.EnsureSuccessStatusCode();
            var dto = (await resp.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;

            var urlResp = await client.GetAsync($"/api/recurring-attachments/occurrences/{dto.AttachmentId}/download-url");
            urlResp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await urlResp.Content.ReadAsStringAsync()).Should().Contain("fake-storage.test");
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

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // userId is the external claim (oid); look up the internal DB user via UserLogins.
            var login = await db.UserLogins.AsNoTracking()
                .SingleAsync(ul => ul.Provider == "https://test.local" && ul.ExternalId == userId.ToString());
            var seriesId = (await db.RecurringTaskSeries.AsNoTracking()
                .SingleAsync(s => s.UserId == login.UserId)).Id;
            return (userId, seriesId, client);
        }

        private async Task<UploadRecurringAttachmentResultDto> UploadSeriesAttachmentAsync(
            HttpClient client, Guid seriesId, string fileName)
        {
            using var content = BuildMultipart(fileName, "application/pdf", new byte[] { 1, 2, 3 });
            var resp = await client.PostAsync($"/api/recurring-attachments/series/{seriesId}", content);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<UploadRecurringAttachmentResultDto>())!;
        }

        private static MultipartFormDataContent BuildMultipart(string fileName, string contentType, byte[] bytes)
        {
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);
            return content;
        }

        private static Task<HttpResponseMessage> UploadToOccurrenceAsync(
            HttpClient client, Guid seriesId, DateOnly occurrenceDate,
            string fileName, string contentType, byte[] bytes)
        {
            var content = BuildMultipart(fileName, contentType, bytes);
            return client.PostAsync(
                $"/api/recurring-attachments/occurrences/{seriesId}/{occurrenceDate:yyyy-MM-dd}",
                content);
        }
    }
}
