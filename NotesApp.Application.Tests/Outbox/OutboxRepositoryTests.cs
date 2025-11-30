using FluentAssertions;
using NotesApp.Application.Abstractions.Persistence;
using NotesApp.Application.Tests.Infrastructure;
using NotesApp.Domain;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using NotesApp.Infrastructure.Persistence.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Outbox
{
    /// <summary>
    /// Tests for OutboxRepository using a SQL Server test AppDbContext.
    /// Focuses on GetPendingBatchAsync filtering, ordering, and limiting.
    /// </summary>
    public sealed class OutboxRepositoryTests
    {
        // Small test-only aggregate to avoid involving real Note/Task types.
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
        public async Task GetPendingBatchAsync_returns_only_unprocessed_messages_ordered_by_createdAt_and_limited_by_maxCount()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            IOutboxRepository repository = new OutboxRepository(context);

            var userId = Guid.NewGuid();
            var utcBase = DateTime.UtcNow;

            OutboxMessage CreateMessage(int minutesOffset, bool processed)
            {
                var utcNow = utcBase.AddMinutes(minutesOffset);
                var aggregate = new TestCalendarEntity(userId, new DateOnly(2025, 2, 20), utcNow);

                var result = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                    aggregate,
                    TestEvent.Created,
                    """{"x":1}""",
                    utcNow);

                result.IsSuccess.Should().BeTrue();
                var msg = result.Value!;

                if (processed)
                {
                    msg.MarkProcessed(utcNow.AddMinutes(1));
                }

                return msg;
            }

            // Unprocessed messages with different CreatedAtUtc
            var m1 = CreateMessage(0, processed: false);   // oldest pending
            var m2 = CreateMessage(1, processed: false);
            var m3 = CreateMessage(2, processed: false);   // newest pending

            // Processed messages that should be ignored
            var processedBefore = CreateMessage(-1, processed: true);
            var processedAfter = CreateMessage(3, processed: true);

            await context.OutboxMessages.AddRangeAsync(
                m3, m1, m2,
                processedBefore, processedAfter);

            await context.SaveChangesAsync();

            var maxCount = 2;

            // Act
            var resultBatch = await repository.GetPendingBatchAsync(maxCount, CancellationToken.None);

            // Assert
            resultBatch.Should().NotBeNull();
            resultBatch.Should().HaveCount(2);

            // Only unprocessed
            resultBatch.All(m => m.ProcessedAtUtc == null).Should().BeTrue();

            // Ordered by CreatedAtUtc ascending and limited
            var idsInOrder = resultBatch.Select(m => m.Id).ToList();
            idsInOrder.Should().ContainInOrder(m1.Id, m2.Id);
        }

        [Fact]
        public async Task GetPendingBatchAsync_when_no_unprocessed_messages_returns_empty_list()
        {
            // Arrange
            await using var context = SqlServerAppDbContextFactory.CreateContext();

            IOutboxRepository repository = new OutboxRepository(context);

            var userId = Guid.NewGuid();
            var utcNow = DateTime.UtcNow;

            var aggregate = new TestCalendarEntity(userId, new DateOnly(2025, 2, 20), utcNow);

            var createResult = OutboxMessage.Create<TestCalendarEntity, TestEvent>(
                aggregate,
                TestEvent.Created,
                """{"x":1}""",
                utcNow);

            createResult.IsSuccess.Should().BeTrue();
            var processedMessage = createResult.Value!;

            // Mark as processed so it should not be returned
            processedMessage.MarkProcessed(utcNow.AddMinutes(5));

            await context.OutboxMessages.AddAsync(processedMessage);
            await context.SaveChangesAsync();

            // Act
            var resultBatch = await repository.GetPendingBatchAsync(
                maxCount: 10,
                cancellationToken: CancellationToken.None);

            // Assert
            resultBatch.Should().NotBeNull();
            resultBatch.Should().BeEmpty();
        }
    }
}
