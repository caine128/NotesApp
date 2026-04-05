using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the Subtask entity.
    ///
    /// Key decisions:
    /// - Global query filter hides soft-deleted subtasks from all normal queries.
    /// - Filtered index on (UserId, TaskId) optimises loading all subtasks for a task.
    /// - Composite index on (UserId, UpdatedAtUtc) optimises sync pull queries.
    /// - FK from TaskId to Tasks.Id uses DeleteBehavior.Cascade as a DB-level safety net:
    ///   if a TaskItem row is ever physically removed (e.g. data-maintenance scripts),
    ///   SQL Server will automatically hard-delete the orphaned Subtask rows.
    ///   All normal application paths soft-delete the task (IsDeleted = true) and
    ///   cascade-soft-delete subtasks at the application layer.
    /// </summary>
    public sealed class SubtaskConfiguration : IEntityTypeConfiguration<Subtask>
    {
        public void Configure(EntityTypeBuilder<Subtask> builder)
        {
            builder.ToTable("Subtasks");

            // Primary key
            builder.HasKey(s => s.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(s => s.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Core domain properties
            // -------------------------

            builder.Property(s => s.UserId)
                   .IsRequired();

            builder.Property(s => s.TaskId)
                   .IsRequired();

            builder.Property(s => s.Text)
                   .IsRequired()
                   .HasMaxLength(Subtask.MaxTextLength);

            builder.Property(s => s.IsCompleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            builder.Property(s => s.Position)
                   .IsRequired()
                   .HasMaxLength(Subtask.MaxPositionLength);

            builder.Property(s => s.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(s => s.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(s => s.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(s => s.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Foreign key
            // -------------------------

            // DeleteBehavior.Cascade: DB-level safety net for physical row deletion.
            // Application-layer soft-delete cascades are handled separately in the
            // REST DeleteTaskCommandHandler (SoftDeleteAllForTaskAsync) and the sync
            // push handler (client sends explicit SubtaskDeleted items).
            builder.HasOne<TaskItem>()
                   .WithMany()
                   .HasForeignKey(s => s.TaskId)
                   .OnDelete(DeleteBehavior.Cascade);

            // -------------------------
            // Global query filter
            // -------------------------

            // Hide soft-deleted subtasks from all normal queries by default.
            // GetChangedSinceAsync uses IgnoreQueryFilters() to surface deleted
            // subtasks in the sync-deleted bucket.
            builder.HasQueryFilter(s => !s.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Load all active subtasks for a task (GetTaskDetail, GetAllForTaskAsync)
            builder.HasIndex(s => new { s.UserId, s.TaskId })
                   .HasFilter("[IsDeleted] = 0")
                   .HasDatabaseName("IX_Subtasks_UserId_TaskId");

            // 2) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(s => new { s.UserId, s.UpdatedAtUtc })
                   .HasDatabaseName("IX_Subtasks_UserId_UpdatedAtUtc");
        }
    }
}
