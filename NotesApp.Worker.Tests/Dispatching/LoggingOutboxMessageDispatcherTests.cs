using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Domain;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Worker.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Moq;
using System.Text.Json;

namespace NotesApp.Worker.Tests.Dispatching
{
    public sealed class LoggingOutboxMessageDispatcherTests
    {
        // Same small test-only aggregate pattern used in OutboxRepositoryTests
        private sealed class TestCalendarEntity : Entity<Guid>, ICalendarEntity
        {
            public Guid UserId { get; private set; }
            public DateOnly Date { get; private set; }

            long ICalendarEntity.Version => throw new NotImplementedException();

            public TestCalendarEntity(Guid userId, DateOnly date, DateTime utcNow)
                : base(Guid.NewGuid(), utcNow)
            {
                UserId = userId;
                Date = date;
            }
        }

        private enum TestEvent
        {
            Created,
            Updated
        }

        private static OutboxMessage CreateOutboxMessageForTests(
            Guid userId,
            string messageType,
            string payload)
        {
            var utcNow = DateTime.UtcNow;
            var aggregate = new TestCalendarEntity(userId, new DateOnly(2025, 1, 1), utcNow);

            var result = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                """{"dummy":1}""",
                utcNow);

            result.IsSuccess.Should().BeTrue();
            var msg = result.Value!;

            // Override MessageType and Payload via reflection for test scenarios
            typeof(OutboxMessage)
                .GetProperty(nameof(OutboxMessage.MessageType), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(msg, messageType);

            typeof(OutboxMessage)
                .GetProperty(nameof(OutboxMessage.Payload), BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(msg, payload);

            return msg;
        }

        private static LoggingOutboxMessageDispatcher CreateSut(
            Mock<IPushNotificationService> pushServiceMock,
            Mock<ILogger<LoggingOutboxMessageDispatcher>> loggerMock)
        {
            return new LoggingOutboxMessageDispatcher(
                loggerMock.Object,
                pushServiceMock.Object);
        }

        [Fact]
        public async Task DispatchAsync_for_sync_related_message_with_originDeviceId_calls_push_service()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LoggingOutboxMessageDispatcher>>();
            var pushServiceMock = new Mock<IPushNotificationService>();

            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();

            var payload = JsonSerializer.Serialize(new
            {
                OriginDeviceId = originDeviceId
            });

            // Note.Created is considered sync-related
            var message = CreateOutboxMessageForTests(
                userId,
                messageType: "Note.Created",
                payload: payload);

            pushServiceMock
                .Setup(s => s.SendSyncNeededAsync(userId, originDeviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok());

            var sut = CreateSut(pushServiceMock, loggerMock);

            // Act
            var result = await sut.DispatchAsync(message, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            pushServiceMock.Verify(
                s => s.SendSyncNeededAsync(
                    userId,
                    originDeviceId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_for_sync_related_message_without_originDeviceId_passes_null_to_push_service()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LoggingOutboxMessageDispatcher>>();
            var pushServiceMock = new Mock<IPushNotificationService>();

            var userId = Guid.NewGuid();

            var payload = JsonSerializer.Serialize(new
            {
                SomeOther = 42
            });

            var message = CreateOutboxMessageForTests(
                userId,
                messageType: "TaskItem.Updated",
                payload: payload);

            pushServiceMock
                .Setup(s => s.SendSyncNeededAsync(userId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok());

            var sut = CreateSut(pushServiceMock, loggerMock);

            // Act
            var result = await sut.DispatchAsync(message, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            pushServiceMock.Verify(
                s => s.SendSyncNeededAsync(
                    userId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_for_non_sync_message_does_not_call_push_service()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LoggingOutboxMessageDispatcher>>();
            var pushServiceMock = new Mock<IPushNotificationService>();

            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();

            var payload = JsonSerializer.Serialize(new
            {
                OriginDeviceId = originDeviceId
            });

            // Assume Note.EmbeddingRequested is *not* sync-related
            var message = CreateOutboxMessageForTests(
                userId,
                messageType: "Note.EmbeddingRequested",
                payload: payload);

            var sut = CreateSut(pushServiceMock, loggerMock);

            // Act
            var result = await sut.DispatchAsync(message, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            pushServiceMock.Verify(
                s => s.SendSyncNeededAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task DispatchAsync_when_push_service_returns_failure_still_succeeds_to_avoid_worker_retries()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LoggingOutboxMessageDispatcher>>();
            var pushServiceMock = new Mock<IPushNotificationService>();

            var userId = Guid.NewGuid();
            var originDeviceId = Guid.NewGuid();

            var payload = JsonSerializer.Serialize(new
            {
                OriginDeviceId = originDeviceId
            });

            var message = CreateOutboxMessageForTests(
                userId,
                messageType: "Note.Updated",
                payload: payload);

            pushServiceMock
                .Setup(s => s.SendSyncNeededAsync(userId, originDeviceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Fail("Simulated push failure"));

            var sut = CreateSut(pushServiceMock, loggerMock);

            // Act
            var result = await sut.DispatchAsync(message, CancellationToken.None);

            // Assert
            // Design choice: Outbox dispatch stays "successful" even if push fails,
            // to avoid endless retries for transient push issues.
            result.IsSuccess.Should().BeTrue();

            pushServiceMock.Verify(
                s => s.SendSyncNeededAsync(
                    userId,
                    originDeviceId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_with_malformed_payload_treats_originDeviceId_as_null_and_calls_push_service()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LoggingOutboxMessageDispatcher>>();
            var pushServiceMock = new Mock<IPushNotificationService>();

            var userId = Guid.NewGuid();

            // Invalid JSON
            var payload = "not-a-json";

            var message = CreateOutboxMessageForTests(
                userId,
                messageType: "TaskItem.Created",
                payload: payload);

            pushServiceMock
                .Setup(s => s.SendSyncNeededAsync(userId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Ok());

            var sut = CreateSut(pushServiceMock, loggerMock);

            // Act
            var result = await sut.DispatchAsync(message, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();

            pushServiceMock.Verify(
                s => s.SendSyncNeededAsync(
                    userId,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
