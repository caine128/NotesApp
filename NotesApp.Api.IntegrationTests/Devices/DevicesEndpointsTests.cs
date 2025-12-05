using FluentAssertions;
using NotesApp.Api.IntegrationTests.Infrastructure.Auth;
using NotesApp.Api.IntegrationTests.Infrastructure.Hosting;
using NotesApp.Application.Devices.Models;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NotesApp.Api.IntegrationTests.Devices
{
    /// <summary>
    /// End-to-end tests for the Devices API:
    /// - Register device
    /// - List devices for current user
    /// - Unregister device
    /// - Multi-user isolation and token reassignment
    /// - Auth / scope enforcement
    /// </summary>
    public sealed class DevicesEndpointsTests : IClassFixture<NotesAppApiFactory>
    {
        private readonly NotesAppApiFactory _factory;
        private readonly HttpClient _client;

        public DevicesEndpointsTests(NotesAppApiFactory factory)
        {
            _factory = factory;
            var userId = Guid.NewGuid();
            _client = _factory.CreateClientAsUser(userId);
        }

        [Fact]
        public async Task Register__And_GetDevices_roundtrip_succeeds()
        {
            // Arrange
            var payload = new
            {
                DeviceToken = "token-123",
                Platform = DevicePlatform.Android,
                DeviceName = "My Phone"
            };

            // Act 1: register device
            var registerResponse = await _client.PostAsJsonAsync("/api/devices", payload);

            // Assert 1: success + UserDeviceDto
            registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var created =
                await registerResponse.Content.ReadFromJsonAsync<UserDeviceDto>();

            created.Should().NotBeNull();
            created!.Id.Should().NotBeEmpty();
            created.DeviceToken.Should().Be("token-123");
            created.Platform.Should().Be(DevicePlatform.Android);
            created.DeviceName.Should().Be("My Phone");
            created.IsActive.Should().BeTrue();

            var deviceId = created.Id;

            // Act 2: list devices
            var listResponse = await _client.GetAsync("/api/devices");

            // Assert 2
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices =
                await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            devices.Should().NotBeNull();
            devices!.Should().ContainSingle(d => d.Id == deviceId);

            var listed = devices.Single(d => d.Id == deviceId);
            listed.DeviceToken.Should().Be("token-123");
            listed.Platform.Should().Be(DevicePlatform.Android);
            listed.DeviceName.Should().Be("My Phone");
            listed.IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task Register_same_token_twice_for_same_user_is_idempotent()
        {
            // Arrange
            var payload1 = new
            {
                DeviceToken = "token-same-user",
                Platform = DevicePlatform.Android,
                DeviceName = "Original Name"
            };

            var payload2 = new
            {
                DeviceToken = "token-same-user",
                Platform = DevicePlatform.Android,
                DeviceName = "Updated Name"
            };

            // Act 1: initial registration
            var firstResponse = await _client.PostAsJsonAsync("/api/devices", payload1);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstDto =
                await firstResponse.Content.ReadFromJsonAsync<UserDeviceDto>();

            firstDto.Should().NotBeNull();
            var firstId = firstDto!.Id;

            // Act 2: register again with same token for same user
            var secondResponse = await _client.PostAsJsonAsync("/api/devices", payload2);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondDto =
                await secondResponse.Content.ReadFromJsonAsync<UserDeviceDto>();

            secondDto.Should().NotBeNull();
            var secondId = secondDto!.Id;

            // Assert: same device, updated name, still active
            secondId.Should().Be(firstId);
            secondDto.DeviceToken.Should().Be("token-same-user");
            secondDto.DeviceName.Should().Be("Updated Name");
            secondDto.IsActive.Should().BeTrue();

            // List should contain only one device with that token
            var listResponse = await _client.GetAsync("/api/devices");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices =
                await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            devices.Should().NotBeNull();
            var list = devices!.Where(d => d.DeviceToken == "token-same-user").ToList();
            list.Should().HaveCount(1);
            list[0].Id.Should().Be(firstId);
            list[0].DeviceName.Should().Be("Updated Name");
        }

        [Fact]
        public async Task Register_same_token_for_different_users_reassigns_device()
        {
            // Arrange
            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();

            var user1Client = _factory.CreateClientAsUser(user1Id);
            var user2Client = _factory.CreateClientAsUser(user2Id);

            var user1Payload = new
            {
                DeviceToken = "shared-token",
                Platform = DevicePlatform.Android,
                DeviceName = "User1 Device"
            };

            var user2Payload = new
            {
                DeviceToken = "shared-token",
                Platform = DevicePlatform.Android,
                DeviceName = "User2 Device"
            };

            // Act 1: User 1 registers shared token
            var user1RegisterResponse =
                await user1Client.PostAsJsonAsync("/api/devices", user1Payload);
            user1RegisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user1Device =
                await user1RegisterResponse.Content.ReadFromJsonAsync<UserDeviceDto>();
            user1Device.Should().NotBeNull();

            var deviceId = user1Device!.Id;

            // Act 2: User 2 registers same token
            var user2RegisterResponse =
                await user2Client.PostAsJsonAsync("/api/devices", user2Payload);
            user2RegisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user2Device =
                await user2RegisterResponse.Content.ReadFromJsonAsync<UserDeviceDto>();
            user2Device.Should().NotBeNull();

            // Assert: same physical device row reassigned to user 2
            user2Device!.Id.Should().Be(deviceId);
            user2Device.DeviceToken.Should().Be("shared-token");
            user2Device.DeviceName.Should().Be("User2 Device");
            user2Device.IsActive.Should().BeTrue();

            // User 1 should no longer have active devices with that token
            var user1ListResponse = await user1Client.GetAsync("/api/devices");
            user1ListResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user1Devices =
                await user1ListResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            user1Devices.Should().NotBeNull();
            user1Devices!.Should().NotContain(d => d.DeviceToken == "shared-token");

            // User 2 should see the device
            var user2ListResponse = await user2Client.GetAsync("/api/devices");
            user2ListResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var user2Devices =
                await user2ListResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            user2Devices.Should().NotBeNull();
            user2Devices!.Should().ContainSingle(d => d.DeviceToken == "shared-token");
        }

        [Fact]
        public async Task Unregister_existing_device_returns_NoContent_and_hides_from_list()
        {
            // Arrange: register a device
            var payload = new
            {
                DeviceToken = "token-to-delete",
                Platform = DevicePlatform.Android,
                DeviceName = "To be removed"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/devices", payload);
            registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var created =
                await registerResponse.Content.ReadFromJsonAsync<UserDeviceDto>();

            created.Should().NotBeNull();
            var deviceId = created!.Id;

            // Act: DELETE /api/devices/{id}
            var deleteResponse = await _client.DeleteAsync($"/api/devices/{deviceId}");

            // Assert: 204 NoContent
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // List should no longer contain the device
            var listResponse = await _client.GetAsync("/api/devices");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices =
                await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            devices.Should().NotBeNull();
            devices!.Should().NotContain(d => d.Id == deviceId);
        }

        [Fact]
        public async Task Unregister_nonexistent_device_returns_bad_request()
        {
            // Arrange
            var nonExistingDeviceId = Guid.NewGuid();

            // Act
            var response = await _client.DeleteAsync($"/api/devices/{nonExistingDeviceId}");

            // Assert
            // Currently Device.NotFound is mapped to a generic failure -> 400 BadRequest.
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Cannot_unregister_device_belonging_to_another_user()
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var attackerId = Guid.NewGuid();

            var ownerClient = _factory.CreateClientAsUser(ownerId);
            var attackerClient = _factory.CreateClientAsUser(attackerId);

            var payload = new
            {
                DeviceToken = "owner-device-token",
                Platform = DevicePlatform.Android,
                DeviceName = "Owner Device"
            };

            // Owner registers the device
            var ownerRegisterResponse =
                await ownerClient.PostAsJsonAsync("/api/devices", payload);

            ownerRegisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var ownerDevice =
                await ownerRegisterResponse.Content.ReadFromJsonAsync<UserDeviceDto>();

            ownerDevice.Should().NotBeNull();
            var deviceId = ownerDevice!.Id;

            // Act: attacker tries to delete owner's device
            var attackerDeleteResponse =
                await attackerClient.DeleteAsync($"/api/devices/{deviceId}");

            // Assert: currently mapped as generic failure -> 400 BadRequest
            attackerDeleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Register_device_with_invalid_payload_returns_bad_request()
        {
            // Arrange: invalid because token is whitespace and platform is Unknown (0)
            var payload = new
            {
                DeviceToken = "   ",
                Platform = DevicePlatform.Unknown,
                DeviceName = (string?)null
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/devices", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Get_devices_without_auth_returns_unauthorized()
        {
            // Arrange: raw client without test headers
            var unauthenticatedClient = _factory.CreateClient();

            // Act
            var response = await unauthenticatedClient.GetAsync("/api/devices");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Get_devices_with_required_scope_returns_ok()
        {
            // Arrange: client with user id + correct scope header
            var client = _factory.CreateClient();

            var userId = Guid.NewGuid();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.ScopeHeaderName,
                "api://d1047ffd-a054-4a9f-aeb0-198996f0c0c6/notes.readwrite");

            // Act
            var response = await client.GetAsync("/api/devices");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Get_devices_without_required_scope_returns_forbidden()
        {
            // Arrange: client with user id but no scope
            var client = _factory.CreateClient();

            var userId = Guid.NewGuid();
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeaderName, userId.ToString());
            // No X-Test-Scopes header => user authenticated but missing required scope

            // Act
            var response = await client.GetAsync("/api/devices");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Get_devices_returns_empty_list_when_user_has_no_devices()
        {
            // Arrange: fresh user with no devices
            var userId = Guid.NewGuid();
            var client = _factory.CreateClientAsUser(userId);

            // Act
            var response = await client.GetAsync("/api/devices");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var devices =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<UserDeviceDto>>();

            devices.Should().NotBeNull();
            devices!.Should().BeEmpty();
        }
    }
}
