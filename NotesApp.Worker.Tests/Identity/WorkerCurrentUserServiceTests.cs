using FluentAssertions;
using NotesApp.Application.Common.Interfaces;
using NotesApp.Worker.Identity;
using NotesApp.Worker.Outbox;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker.Tests.Identity
{
    public sealed class WorkerCurrentUserServiceTests
    {
        private sealed class FakeOutboxProcessingContextAccessor : IOutboxProcessingContextAccessor
        {
            public OutboxProcessingContext? Current { get; private set; }

            public void Set(OutboxProcessingContext? context)
            {
                Current = context;
            }
        }

        [Fact]
        public async Task GetUserIdAsync_with_context_returns_user_id()
        {
            // Arrange
            var fakeAccessor = new FakeOutboxProcessingContextAccessor();
            var userId = Guid.NewGuid();
            var ctx = new OutboxProcessingContext(Guid.NewGuid(), userId);

            fakeAccessor.Set(ctx);

            ICurrentUserService service = new WorkerCurrentUserService(fakeAccessor);

            // Act
            var result = await service.GetUserIdAsync(CancellationToken.None);

            // Assert
            result.Should().Be(userId);
        }

        [Fact]
        public async Task GetUserIdAsync_without_context_throws_InvalidOperationException()
        {
            // Arrange
            var fakeAccessor = new FakeOutboxProcessingContextAccessor();
            ICurrentUserService service = new WorkerCurrentUserService(fakeAccessor);

            // Act
            Func<Task> act = async () => await service.GetUserIdAsync(CancellationToken.None);

            // Assert
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*OutboxProcessingContext*");
        }
    }
}
