using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Assets.Models;
using NotesApp.Application.Devices.Commands.RegisterDevice;
using NotesApp.Application.Devices.Models;
using NotesApp.Application.Sync.Models;
using NotesApp.Domain.Common;
using NotesApp.Domain.Users;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NotesApp.Api.IntegrationTests.Assets
{
    /// <summary>
    /// Integration tests that verify the full asset lifecycle against real Azure Blob Storage.
    ///
    /// These tests are decorated with [Trait("Category", "RealAzure")] so they can be
    /// run selectively and excluded from standard CI pipelines that don't have Azure credentials.
    ///
    /// Run selectively with:
    ///   dotnet test --filter "Category=RealAzure"
    ///
    /// Prerequisites (see AzureNotesAppApiFactory for full details):
    ///   - az login completed with an account that has Storage Blob Data Contributor
    ///     and Storage Blob Delegator roles on the notesappstore storage account.
    ///   - ConnectionStrings:AzureBlobStorage removed from secrets.json.
    /// </summary>
    [Trait("Category", "RealAzure")]
    public sealed class RealAzureBlobStorageTests : IClassFixture<AzureNotesAppApiFactory>
    {
        private readonly AzureNotesAppApiFactory _factory;

        public RealAzureBlobStorageTests(AzureNotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Upload_asset_to_real_azure_and_receive_valid_sas_download_url()
        {
            // ── Arrange: user with one device ────────────────────────────────
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var device = await RegisterDeviceAsync(client, "real-azure-device-" + userId, "TestPhone");

            // ── Arrange: push a note with one image block ────────────────────
            var noteClientId = Guid.NewGuid();
            var imageBlockClientId = Guid.NewGuid();
            var assetClientId = "real-azure-" + Guid.NewGuid().ToString("N");
            const int FileSizeBytes = 256;

            var pushPayload = new SyncPushCommandPayloadDto
            {
                DeviceId = device.Id,
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Notes = new SyncPushNotesDto
                {
                    Created =
                    [
                        new NoteCreatedPushItemDto
                        {
                            ClientId = noteClientId,
                            Date     = new DateOnly(2025, 8, 1),
                            Title    = "Real Azure Blob Test"
                        }
                    ]
                },
                Blocks = new SyncPushBlocksDto
                {
                    Created =
                    [
                        new BlockCreatedPushItemDto
                        {
                            ClientId         = imageBlockClientId,
                            ParentClientId   = noteClientId,
                            ParentType       = BlockParentType.Note,
                            Type             = BlockType.Image,
                            Position         = "a0",
                            AssetClientId    = assetClientId,
                            AssetFileName    = "azure-test.jpg",
                            AssetContentType = "image/jpeg",
                            AssetSizeBytes   = FileSizeBytes
                        }
                    ]
                }
            };

            var pushResponse = await client.PostAsJsonAsync("/api/sync/push", pushPayload);
            pushResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var pushResult = await pushResponse.Content.ReadFromJsonAsync<SyncPushResultDto>();
            pushResult.Should().NotBeNull();

            var imageBlockServerId = pushResult!.Blocks.Created
                .Single(b => b.ClientId == imageBlockClientId)
                .ServerId;

            // ── Act: upload real bytes to Azure Blob Storage ─────────────────
            var imageBytes = new byte[FileSizeBytes];
            new Random(42).NextBytes(imageBytes);

            using var formContent = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            formContent.Add(fileContent, "file", "azure-test.jpg");

            var uploadResponse = await client.PostAsync(
                $"/api/assets/{imageBlockServerId}?assetClientId={Uri.EscapeDataString(assetClientId)}",
                formContent);

            // TEMPORARY: capture response body to diagnose the 500
            var uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync();
            uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"upload to real Azure should succeed with correct RBAC roles. Response body: {uploadResponseBody}");

            var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadAssetResultDto>();
            uploadResult.Should().NotBeNull();
            uploadResult!.AssetId.Should().NotBeEmpty();
            uploadResult.BlockId.Should().Be(imageBlockServerId);

            // The download URL must be a real Azure SAS URL, not the fake URL.
            uploadResult.DownloadUrl.Should().StartWith("https://notesappstore.blob.core.windows.net",
                because: "real Azure storage should return a URL pointing to the actual storage account");
            uploadResult.DownloadUrl.Should().Contain("sig=",
                because: "the URL must contain a SAS signature generated via user delegation key");

            // ── Assert: the SAS URL is actually reachable ────────────────────
            using var httpClient = new HttpClient();
            var sasResponse = await httpClient.GetAsync(uploadResult.DownloadUrl);
            sasResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                because: "the generated SAS URL should grant read access to the uploaded blob");

            var downloadedBytes = await sasResponse.Content.ReadAsByteArrayAsync();
            downloadedBytes.Should().Equal(imageBytes,
                because: "downloaded content must match what was uploaded");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static async Task<UserDeviceDto> RegisterDeviceAsync(
            HttpClient client, string token, string name)
        {
            var response = await client.PostAsJsonAsync("/api/devices", new RegisterDeviceCommand
            {
                DeviceToken = token,
                Platform = DevicePlatform.Android,
                DeviceName = name
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"registering device '{name}' should succeed");

            var dto = await response.Content.ReadFromJsonAsync<UserDeviceDto>();
            dto.Should().NotBeNull();
            return dto!;
        }
    }
}