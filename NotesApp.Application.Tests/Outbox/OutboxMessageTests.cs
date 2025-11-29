using FluentAssertions;
using NotesApp.Domain;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Outbox
{
    public class OutboxMessageTests
    {
        // a small test-only aggregate to avoid involving Note/Task
        private sealed class TestCalendarEntity : Entity<Guid>, ICalendarEntity
        {
            public Guid UserId { get; private set; }
            public DateOnly Date { get; private set; }

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

        [Fact]
        public void Create_with_valid_input_succeeds_and_sets_all_fields()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;
            var userId = Guid.NewGuid();
            var aggregate = new TestCalendarEntity(userId, new DateOnly(2025, 1, 1), utcNow);
            var payload = """{"foo":"bar"}""";

            // Act
            var result = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                payload,
                utcNow);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            var msg = result.Value!;

            msg.UserId.Should().Be(userId);
            msg.AggregateId.Should().Be(aggregate.Id);
            msg.AggregateType.Should().Be(nameof(TestCalendarEntity));
            msg.MessageType.Should().Be($"{nameof(TestCalendarEntity)}.{TestEvent.Created}");
            msg.Payload.Should().Be(payload);
            msg.AttemptCount.Should().Be(0);
            msg.ProcessedAtUtc.Should().BeNull();
            msg.CreatedAtUtc.Should().Be(utcNow);
            msg.UpdatedAtUtc.Should().Be(utcNow);
        }

        [Fact]
        public void Create_with_null_aggregate_returns_failure()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;

            // Act
            var result = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate: null!,
                eventType: TestEvent.Created,
                payload: """{"x":1}""",
                utcNow: utcNow);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.Aggregate.Null");
        }

        [Fact]
        public void Create_with_empty_payload_returns_failure()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;
            var aggregate = new TestCalendarEntity(Guid.NewGuid(), new DateOnly(2025, 1, 1), utcNow);

            // Act
            var result = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                payload: "   ",    // empty after trim
                utcNow: utcNow);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.Payload.Empty");
        }

        [Fact]
        public void Create_with_empty_user_or_id_returns_failure()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;

            // Aggregate with empty ids via a small derived class
            var badAggregate = new BadAggregate(utcNow);

            // Act
            var result = OutboxMessage.Create<BadAggregate, TestEvent>(
                badAggregate,
                TestEvent.Created,
                """{"x":1}""",
                utcNow);

            // Assert
            result.IsFailure.Should().BeTrue();
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.AggregateId.Empty");
            result.Errors.Should().Contain(e => e.Code == "OutboxMessage.UserId.Empty");
        }

        private sealed class BadAggregate : Entity<Guid>, ICalendarEntity
        {
            public Guid UserId { get; private set; } = Guid.Empty;
            public DateOnly Date { get; private set; } = new DateOnly(2025, 1, 1);

            public BadAggregate(DateTime utcNow) : base(Guid.Empty, utcNow)
            {
            }
        }

        [Fact]
        public void MarkProcessed_sets_processedAt_and_is_idempotent()
        {
            var utcNow = DateTime.UtcNow;
            var aggregate = new TestCalendarEntity(Guid.NewGuid(), new DateOnly(2025, 1, 1), utcNow);
            var create = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                """{"x":1}""",
                utcNow);

            var msg = create.Value!;

            var firstTime = utcNow.AddMinutes(1);
            var secondTime = utcNow.AddMinutes(2);

            var r1 = msg.MarkProcessed(firstTime);
            var r2 = msg.MarkProcessed(secondTime);

            r1.IsSuccess.Should().BeTrue();
            r2.IsSuccess.Should().BeTrue();
            msg.ProcessedAtUtc.Should().Be(secondTime); // last call wins
        }

        [Fact]
        public void IncrementAttempt_increases_attemptCount_and_updates_timestamp()
        {
            var utcNow = DateTime.UtcNow;
            var aggregate = new TestCalendarEntity(Guid.NewGuid(), new DateOnly(2025, 1, 1), utcNow);
            var create = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                """{"x":1}""",
                utcNow);

            var msg = create.Value!;

            var t1 = utcNow.AddMinutes(1);
            var t2 = utcNow.AddMinutes(2);

            msg.AttemptCount.Should().Be(0);

            msg.IncrementAttempt(t1);
            msg.AttemptCount.Should().Be(1);
            msg.UpdatedAtUtc.Should().Be(t1);

            msg.IncrementAttempt(t2);
            msg.AttemptCount.Should().Be(2);
            msg.UpdatedAtUtc.Should().Be(t2);
        }
    }
}
