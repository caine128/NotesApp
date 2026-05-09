using FluentAssertions;
using Moq;
using NotesApp.Application.Common;
using NotesApp.Application.Sync;
using NotesApp.Application.Sync.Abstractions;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NotesApp.Application.Tests.Sync
{
    /// <summary>
    /// Unit tests for SyncChangeWriter — verifies it stages SyncChange entities with correct
    /// metadata and payload shapes, and never calls SaveChanges.
    /// </summary>
    public sealed class SyncChangeWriterTests
    {
        private readonly Mock<ISyncChangeRepository> _repository = new();
        private readonly Mock<ISystemClock> _clock = new();
        private readonly DateTime _utcNow = new(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc);
        private readonly Guid _userId = Guid.NewGuid();

        public SyncChangeWriterTests()
        {
            _clock.Setup(c => c.UtcNow).Returns(_utcNow);
        }

        private SyncChangeWriter Writer() => new(_repository.Object, _clock.Object);

        [Fact]
        public async Task AddCreated_for_TaskItem_stages_SyncChange_with_Task_family_and_serialized_dto()
        {
            var task = TaskItem.Create(_userId, new DateOnly(2026, 5, 3), "buy milk", null, null, null, null, null, null, TaskPriority.Normal, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddCreatedAsync(task, originDeviceId: null);

            captured.Should().NotBeNull();
            captured!.UserId.Should().Be(_userId);
            captured.EntityFamily.Should().Be(SyncEntityFamily.Task);
            captured.EntityId.Should().Be(task.Id);
            captured.Operation.Should().Be(SyncOperation.Created);
            captured.ChangedAtUtc.Should().Be(_utcNow);
            captured.OriginDeviceId.Should().BeNull();

            using var doc = JsonDocument.Parse(captured.PayloadJson);
            doc.RootElement.GetProperty("title").GetString().Should().Be("buy milk");
            doc.RootElement.GetProperty("id").GetGuid().Should().Be(task.Id);
        }

        [Fact]
        public async Task AddUpdated_uses_Updated_operation()
        {
            var task = TaskItem.Create(_userId, new DateOnly(2026, 5, 3), "buy milk", null, null, null, null, null, null, TaskPriority.Normal, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddUpdatedAsync(task, originDeviceId: null);

            captured!.Operation.Should().Be(SyncOperation.Updated);
        }

        [Fact]
        public async Task AddCreated_propagates_originDeviceId()
        {
            var deviceId = Guid.NewGuid();
            var task = TaskItem.Create(_userId, new DateOnly(2026, 5, 3), "T", null, null, null, null, null, null, TaskPriority.Normal, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddCreatedAsync(task, originDeviceId: deviceId);

            captured!.OriginDeviceId.Should().Be(deviceId);
        }

        [Fact]
        public async Task AddDeleted_serializes_id_and_deletedAtUtc_payload()
        {
            var entityId = Guid.NewGuid();
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddDeletedAsync(SyncEntityFamily.Note, entityId, _userId, originDeviceId: null);

            captured.Should().NotBeNull();
            captured!.EntityFamily.Should().Be(SyncEntityFamily.Note);
            captured.Operation.Should().Be(SyncOperation.Deleted);
            captured.EntityId.Should().Be(entityId);

            using var doc = JsonDocument.Parse(captured.PayloadJson);
            doc.RootElement.GetProperty("id").GetGuid().Should().Be(entityId);
            doc.RootElement.GetProperty("deletedAtUtc").GetDateTime().Should().Be(_utcNow);
        }

        [Fact]
        public async Task Note_AddCreated_uses_Note_family()
        {
            var note = Note.Create(_userId, new DateOnly(2026, 5, 3), "title", null, null, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddCreatedAsync(note, originDeviceId: null);

            captured!.EntityFamily.Should().Be(SyncEntityFamily.Note);
            captured.EntityId.Should().Be(note.Id);
        }

        [Fact]
        public async Task AddCreated_for_RecurringTaskException_stages_SyncChange_with_correct_family_and_empty_child_lists()
        {
            var seriesId = Guid.NewGuid();
            var exception = RecurringTaskException.CreateDeletion(_userId, seriesId, new DateOnly(2026, 5, 3), null, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddCreatedAsync(exception, originDeviceId: null);

            captured.Should().NotBeNull();
            captured!.UserId.Should().Be(_userId);
            captured.EntityFamily.Should().Be(SyncEntityFamily.RecurringTaskException);
            captured.EntityId.Should().Be(exception.Id);
            captured.Operation.Should().Be(SyncOperation.Created);

            using var doc = JsonDocument.Parse(captured.PayloadJson);
            doc.RootElement.GetProperty("subtasks").GetArrayLength().Should().Be(0);
            doc.RootElement.GetProperty("attachments").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public async Task AddUpdated_for_RecurringTaskException_stages_SyncChange_with_Updated_operation()
        {
            var seriesId = Guid.NewGuid();
            var exception = RecurringTaskException.CreateDeletion(_userId, seriesId, new DateOnly(2026, 5, 3), null, _utcNow).Value!;
            SyncChange? captured = null;
            _repository.Setup(r => r.AddAsync(It.IsAny<SyncChange>(), It.IsAny<CancellationToken>()))
                       .Callback<SyncChange, CancellationToken>((sc, _) => captured = sc)
                       .Returns(Task.CompletedTask);

            await Writer().AddUpdatedAsync(exception, originDeviceId: null);

            captured!.Operation.Should().Be(SyncOperation.Updated);
        }
    }
}
