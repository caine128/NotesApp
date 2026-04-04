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

namespace NotesApp.Api.IntegrationTests.Notes
{
    /// <summary>
    /// End-to-end tests for the note + block + asset lifecycle:
    ///
    /// Scenario: A user authors a rich note on Device 1 (phone) by pushing a note with
    /// multiple block types — Paragraph, Heading, BulletList, and an Image (asset) block —
    /// then uploads the actual image file. Device 2 (tablet) then performs an initial sync
    /// pull and receives the note, all four blocks, and the uploaded asset.
    ///
    /// This verifies the Blocks and Assets tables are written correctly, which the existing
    /// test suite doesn't cover because no test exercises the block + asset code paths.
    /// </summary>
    public sealed class NoteWithBlocksAndAssetsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;

        public NoteWithBlocksAndAssetsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Rich_note_created_on_device1_appears_with_all_blocks_and_asset_on_device2_sync()
        {
            // ── Shared user (same user, two devices) ─────────────────────────
            var userId = Guid.NewGuid();

            // ── Device 1: register ───────────────────────────────────────────
            var device1Client = _factory.CreateClientAsUser(userId);
            var device1 = await RegisterDeviceAsync(device1Client, "token-phone-" + userId, "Phone");

            // ── Device 1: push a note with four block types ──────────────────
            //
            // All blocks are created in the same push by referencing the note
            // via ParentClientId (the note's client-generated ID) because the
            // server ID for the note is not known until after the push.

            var noteClientId      = Guid.NewGuid();
            var paragraphClientId = Guid.NewGuid();
            var headingClientId   = Guid.NewGuid();
            var bulletClientId    = Guid.NewGuid();
            var imageClientId     = Guid.NewGuid();
            var assetClientId     = "img-" + Guid.NewGuid().ToString("N");

            const int FileSizeBytes = 512;

            var pushPayload = new SyncPushCommandPayloadDto
            {
                DeviceId = device1.Id,
                ClientSyncTimestampUtc = DateTime.UtcNow,
                Notes = new SyncPushNotesDto
                {
                    Created =
                    [
                        new NoteCreatedPushItemDto
                        {
                            ClientId = noteClientId,
                            Date     = new DateOnly(2025, 6, 15),
                            Title    = "Trip to the mountains",
                            Summary  = "Notes from the hike",
                            Tags     = "travel,hiking"
                        }
                    ]
                },
                Blocks = new SyncPushBlocksDto
                {
                    Created =
                    [
                        new BlockCreatedPushItemDto
                        {
                            ClientId      = paragraphClientId,
                            ParentClientId = noteClientId,
                            ParentType    = BlockParentType.Note,
                            Type          = BlockType.Paragraph,
                            Position      = "a0",
                            TextContent   = "We started early in the morning before sunrise."
                        },
                        new BlockCreatedPushItemDto
                        {
                            ClientId      = headingClientId,
                            ParentClientId = noteClientId,
                            ParentType    = BlockParentType.Note,
                            Type          = BlockType.Heading1,
                            Position      = "a1",
                            TextContent   = "The Summit"
                        },
                        new BlockCreatedPushItemDto
                        {
                            ClientId      = bulletClientId,
                            ParentClientId = noteClientId,
                            ParentType    = BlockParentType.Note,
                            Type          = BlockType.BulletList,
                            Position      = "a2",
                            TextContent   = "Packed water\nPacked snacks\nPacked first-aid kit"
                        },
                        new BlockCreatedPushItemDto
                        {
                            ClientId         = imageClientId,
                            ParentClientId   = noteClientId,
                            ParentType       = BlockParentType.Note,
                            Type             = BlockType.Image,
                            Position         = "a3",
                            AssetClientId    = assetClientId,
                            AssetFileName    = "summit.jpg",
                            AssetContentType = "image/jpeg",
                            AssetSizeBytes   = FileSizeBytes
                        }
                    ]
                }
            };

            var pushResponse = await device1Client.PostAsJsonAsync("/api/sync/push", pushPayload);
            pushResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var pushResult = await pushResponse.Content.ReadFromJsonAsync<SyncPushResultDto>();
            pushResult.Should().NotBeNull();

            // Note was created
            pushResult!.Notes.Created.Should().HaveCount(1);
            var noteResult = pushResult.Notes.Created[0];
            noteResult.Status.Should().Be(SyncPushCreatedStatus.Created);
            noteResult.ServerId.Should().NotBeEmpty();
            var noteServerId = noteResult.ServerId;

            // All 4 blocks were created with no conflicts
            pushResult.Blocks.Created.Should().HaveCount(4);
            pushResult.Blocks.Created.Should().OnlyContain(b => b.Status == SyncPushCreatedStatus.Created);

            // Locate the image block server ID — needed for the asset upload
            var imageBlockResult = pushResult.Blocks.Created.Single(b => b.ClientId == imageClientId);
            var imageBlockServerId = imageBlockResult.ServerId;

            // ── Device 1: upload the actual image file ───────────────────────
            //
            // POST /api/assets/{blockId}?assetClientId=...
            // Uses multipart/form-data with a single "file" part.

            var imageBytes = new byte[FileSizeBytes];
            new Random(42).NextBytes(imageBytes); // deterministic fake JPEG bytes

            using var formContent = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            formContent.Add(fileContent, "file", "summit.jpg");

            var uploadResponse = await device1Client.PostAsync(
                $"/api/assets/{imageBlockServerId}?assetClientId={Uri.EscapeDataString(assetClientId)}",
                formContent);

            uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                because: "asset upload should succeed with the in-memory blob store");

            var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadAssetResultDto>();
            uploadResult.Should().NotBeNull();
            uploadResult!.AssetId.Should().NotBeEmpty();
            uploadResult.BlockId.Should().Be(imageBlockServerId);
            uploadResult.DownloadUrl.Should().NotBeNullOrEmpty();

            var assetServerId = uploadResult.AssetId;

            // ── Device 2: register (same user, new device) ───────────────────
            var device2Client = _factory.CreateClientAsUser(userId);
            await RegisterDeviceAsync(device2Client, "token-tablet-" + userId, "Tablet");

            // ── Device 2: initial sync pull (no sinceUtc = full sync) ────────
            var syncResponse = await device2Client.GetAsync("/api/sync/changes");
            syncResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var sync = await syncResponse.Content.ReadFromJsonAsync<SyncChangesDto>();
            sync.Should().NotBeNull();

            // ── Assert: note is present ───────────────────────────────────────
            sync!.Notes.Created.Should().ContainSingle(n => n.Id == noteServerId);
            var syncedNote = sync.Notes.Created.Single(n => n.Id == noteServerId);
            syncedNote.Title.Should().Be("Trip to the mountains");
            syncedNote.Tags.Should().Be("travel,hiking");

            // ── Assert: all 4 blocks are present ─────────────────────────────
            sync.Blocks.Created.Should().HaveCount(4);

            sync.Blocks.Created.Should().Contain(b =>
                b.Type == BlockType.Paragraph &&
                b.ParentId == noteServerId &&
                b.TextContent == "We started early in the morning before sunrise.");

            sync.Blocks.Created.Should().Contain(b =>
                b.Type == BlockType.Heading1 &&
                b.ParentId == noteServerId &&
                b.TextContent == "The Summit");

            sync.Blocks.Created.Should().Contain(b =>
                b.Type == BlockType.BulletList &&
                b.ParentId == noteServerId &&
                b.TextContent == "Packed water\nPacked snacks\nPacked first-aid kit");

            // ── Assert: image block reflects the completed upload ─────────────
            var syncedImageBlock = sync.Blocks.Created.Single(b => b.Type == BlockType.Image);
            syncedImageBlock.ParentId.Should().Be(noteServerId);
            syncedImageBlock.AssetClientId.Should().Be(assetClientId);
            syncedImageBlock.AssetFileName.Should().Be("summit.jpg");
            syncedImageBlock.AssetContentType.Should().Be("image/jpeg");
            syncedImageBlock.UploadStatus.Should().Be(UploadStatus.Synced,
                because: "the file was uploaded successfully by Device 1");
            syncedImageBlock.AssetId.Should().Be(assetServerId);

            // ── Assert: asset record is present in the assets bucket ─────────
            sync.Assets.Created.Should().ContainSingle(a => a.Id == assetServerId);
            var syncedAsset = sync.Assets.Created.Single(a => a.Id == assetServerId);
            syncedAsset.BlockId.Should().Be(imageBlockServerId);
            syncedAsset.FileName.Should().Be("summit.jpg");
            syncedAsset.ContentType.Should().Be("image/jpeg");
            syncedAsset.SizeBytes.Should().Be(FileSizeBytes);
        }

        [Fact]
        public async Task Push_blocks_with_multiple_text_types_persists_all_to_database()
        {
            // Simpler focused test: verifies Blocks table receives rows for
            // each text-based block type without involving the asset upload flow.

            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);
            var device = await RegisterDeviceAsync(client, "token-text-" + userId, "Phone");

            var noteClientId = Guid.NewGuid();

            var blocks = new[]
            {
                (ClientId: Guid.NewGuid(), Type: BlockType.Paragraph,    Position: "a0", Text: "Paragraph text"),
                (ClientId: Guid.NewGuid(), Type: BlockType.Heading1,     Position: "a1", Text: "Heading 1"),
                (ClientId: Guid.NewGuid(), Type: BlockType.Heading2,     Position: "a2", Text: "Heading 2"),
                (ClientId: Guid.NewGuid(), Type: BlockType.Heading3,     Position: "a3", Text: "Heading 3"),
                (ClientId: Guid.NewGuid(), Type: BlockType.BulletList,   Position: "a4", Text: "Bullet item"),
                (ClientId: Guid.NewGuid(), Type: BlockType.NumberedList, Position: "a5", Text: "Numbered item"),
                (ClientId: Guid.NewGuid(), Type: BlockType.Quote,        Position: "a6", Text: "A famous quote"),
                (ClientId: Guid.NewGuid(), Type: BlockType.Code,         Position: "a7", Text: "var x = 1;"),
            };

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
                            Date     = new DateOnly(2025, 7, 1),
                            Title    = "All text block types"
                        }
                    ]
                },
                Blocks = new SyncPushBlocksDto
                {
                    Created = blocks.Select(b => new BlockCreatedPushItemDto
                    {
                        ClientId       = b.ClientId,
                        ParentClientId = noteClientId,
                        ParentType     = BlockParentType.Note,
                        Type           = b.Type,
                        Position       = b.Position,
                        TextContent    = b.Text
                    }).ToArray()
                }
            };

            var pushResponse = await client.PostAsJsonAsync("/api/sync/push", pushPayload);
            pushResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var pushResult = await pushResponse.Content.ReadFromJsonAsync<SyncPushResultDto>();
            pushResult.Should().NotBeNull();
            pushResult!.Notes.Created.Should().HaveCount(1);
            pushResult.Blocks.Created.Should().HaveCount(blocks.Length);
            pushResult.Blocks.Created.Should().OnlyContain(b => b.Status == SyncPushCreatedStatus.Created);

            var noteServerId = pushResult.Notes.Created[0].ServerId;

            // Verify all blocks appear on sync pull
            var syncResponse = await client.GetAsync("/api/sync/changes");
            syncResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var sync = await syncResponse.Content.ReadFromJsonAsync<SyncChangesDto>();
            sync.Should().NotBeNull();

            sync!.Blocks.Created.Should().HaveCount(blocks.Length);
            sync.Blocks.Created.Should().OnlyContain(b => b.ParentId == noteServerId);

            foreach (var (_, type, _, text) in blocks)
            {
                sync.Blocks.Created.Should().Contain(b =>
                    b.Type == type && b.TextContent == text,
                    because: $"block of type {type} with text '{text}' should be returned by sync pull");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static async Task<UserDeviceDto> RegisterDeviceAsync(
            HttpClient client, string token, string name)
        {
            var response = await client.PostAsJsonAsync("/api/devices", new RegisterDeviceCommand
            {
                DeviceToken = token,
                Platform    = DevicePlatform.Android,
                DeviceName  = name
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"registering device '{name}' should succeed");

            var dto = await response.Content.ReadFromJsonAsync<UserDeviceDto>();
            dto.Should().NotBeNull();
            return dto!;
        }
    }
}
