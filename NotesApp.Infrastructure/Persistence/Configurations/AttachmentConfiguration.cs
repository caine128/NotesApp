using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the Attachment entity.
    ///
    /// Key decisions:
    /// - Global query filter hides soft-deleted attachments from all normal queries.
    /// - Filtered index on (UserId, TaskId) optimises loading all attachments for a task.
    /// - Composite index on (UserId, UpdatedAtUtc) optimises sync pull queries.
    /// - Index on BlobPath supports the orphan-cleanup background worker.
    /// - FK from TaskId to Tasks.Id uses DeleteBehavior.Cascade as a DB-level safety net:
    ///   if a TaskItem row is ever physically removed (e.g. data-maintenance scripts),
    ///   SQL Server will automatically hard-delete the orphaned Attachment rows.
    ///   All normal application paths soft-delete the task and cascade-soft-delete
    ///   attachments at the application layer (SoftDeleteAllForTaskAsync).
    /// </summary>
    // REFACTORED: added AttachmentConfiguration for task-attachments feature
    public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
    {
        public void Configure(EntityTypeBuilder<Attachment> builder)
        {
            builder.ToTable("Attachments");

            // Primary key
            builder.HasKey(a => a.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(a => a.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Core domain properties
            // -------------------------

            builder.Property(a => a.UserId)
                   .IsRequired();

            builder.Property(a => a.TaskId)
                   .IsRequired();

            builder.Property(a => a.FileName)
                   .IsRequired()
                   .HasMaxLength(Attachment.MaxFileNameLength);

            builder.Property(a => a.ContentType)
                   .IsRequired()
                   .HasMaxLength(Attachment.MaxContentTypeLength);

            builder.Property(a => a.SizeBytes)
                   .IsRequired();

            builder.Property(a => a.BlobPath)
                   .IsRequired()
                   .HasMaxLength(Attachment.MaxBlobPathLength);

            builder.Property(a => a.DisplayOrder)
                   .IsRequired();

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(a => a.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(a => a.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(a => a.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Foreign key
            // -------------------------

            // DeleteBehavior.Cascade: DB-level safety net for physical row deletion.
            // Application-layer soft-delete cascades are handled in DeleteTaskCommandHandler
            // (SoftDeleteAllForTaskAsync) and via the sync push handler (client sends explicit
            // AttachmentDeleted items alongside TaskDeleted in the same push payload).
            builder.HasOne<TaskItem>()
                   .WithMany()
                   .HasForeignKey(a => a.TaskId)
                   .OnDelete(DeleteBehavior.Cascade);

            // -------------------------
            // Global query filter
            // -------------------------

            // Hide soft-deleted attachments from all normal queries by default.
            // GetChangedSinceAsync uses IgnoreQueryFilters() to surface deleted
            // attachments in the sync-deleted bucket.
            builder.HasQueryFilter(a => !a.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Load all active attachments for a task (GetTaskDetail, GetAllForTaskAsync)
            builder.HasIndex(a => new { a.UserId, a.TaskId })
                   .HasFilter("[IsDeleted] = 0")
                   .HasDatabaseName("IX_Attachments_UserId_TaskId");

            // 2) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(a => new { a.UserId, a.UpdatedAtUtc })
                   .HasDatabaseName("IX_Attachments_UserId_UpdatedAtUtc");

            // 3) Blob path lookup (for orphan-cleanup background worker)
            builder.HasIndex(a => a.BlobPath)
                   .HasDatabaseName("IX_Attachments_BlobPath");
        }
    }
}
