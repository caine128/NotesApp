using FluentAssertions;
using NotesApp.Worker.Outbox;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Tests.Outbox
{
    public sealed class OutboxProcessingContextAccessorTests
    {
        [Fact]
        public void Current_is_null_by_default()
        {
            // Arrange
            var accessor = new OutboxProcessingContextAccessor();

            // Act
            var current = accessor.Current;

            // Assert
            current.Should().BeNull();
        }

        [Fact]
        public void Set_sets_and_clears_current_context()
        {
            // Arrange
            var accessor = new OutboxProcessingContextAccessor();
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var context = new OutboxProcessingContext(messageId, userId);

            // Act
            accessor.Set(context);
            var afterSet = accessor.Current;

            accessor.Set(null);
            var afterClear = accessor.Current;

            // Assert
            afterSet.Should().BeSameAs(context);
            afterClear.Should().BeNull();
        }

        [Fact]
        public async Task Context_flows_through_async_calls_in_same_flow()
        {
            // Arrange
            var accessor = new OutboxProcessingContextAccessor();
            var messageId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var context = new OutboxProcessingContext(messageId, userId);

            accessor.Set(context);

            // Act
            var resultUserId = await NestedAsync(accessor);

            // Assert
            resultUserId.Should().Be(userId);

            // Local function that simulates an async call chain
            static async Task<Guid> NestedAsync(IOutboxProcessingContextAccessor acc)
            {
                await Task.Delay(10);
                acc.Current.Should().NotBeNull();
                return acc.Current!.UserId;
            }
        }

        [Fact]
        public async Task Different_async_flows_have_independent_contexts()
        {
            // Arrange
            var accessor = new OutboxProcessingContextAccessor();
            var ctxA = new OutboxProcessingContext(Guid.NewGuid(), Guid.NewGuid());
            var ctxB = new OutboxProcessingContext(Guid.NewGuid(), Guid.NewGuid());

            // Act
            var taskA = Task.Run(async () =>
            {
                accessor.Set(ctxA);
                await Task.Delay(10);
                return accessor.Current;
            });

            var taskB = Task.Run(async () =>
            {
                accessor.Set(ctxB);
                await Task.Delay(10);
                return accessor.Current;
            });

            var resultA = await taskA;
            var resultB = await taskB;

            // Assert
            resultA.Should().BeSameAs(ctxA);
            resultB.Should().BeSameAs(ctxB);
            resultA.Should().NotBeSameAs(resultB);
        }
    }
}
