using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the RecurringTaskAttachment entity.
    ///
    /// RecurringTaskAttachment serves dual purpose via two nullable FKs:
    /// - SeriesId set, ExceptionId null  → series template attachment (inherited by all occurrences)
    /// - ExceptionId set, SeriesId null  → exception attachment override (for one specific occurrence)
    ///
    /// Exactly one FK must be non-null — enforced by a DB-level check constraint.
    ///
    /// Key decisions:
    /// - FK SeriesId uses DeleteBehavior.Cascade (physical series delete removes template attachments).
    /// - FK ExceptionId uses DeleteBehavior.Restrict (SQL Server multi-cascade limitation).
    ///   Application layer cascades via SoftDeleteAllForExceptionAsync.
    /// - Global query filter hides soft-deleted rows from all normal queries.
    /// - No Version column — attachments are immutable after creation.
    /// - Blobs are shared by ThisAndFollowing copies (same BlobPath, different IDs).
    ///   The orphan-cleanup worker checks ExistsNonDeletedWithBlobPathAsync before removing a blob.
    /// </summary>
    // REFACTORED: added for recurring-task-attachments feature
    public sealed class RecurringTaskAttachmentConfiguration : IEntityTypeConfiguration<RecurringTaskAttachment>
    {
        public void Configure(EntityTypeBuilder<RecurringTaskAttachment> builder)
        {
            builder.ToTable("RecurringTaskAttachments", t =>
            {
                // Enforce the dual-FK invariant at the DB level:
                // exactly one of (SeriesId, ExceptionId) must be non-null.
                t.HasCheckConstraint(
                    "CK_RecurringTaskAttachments_ExactlyOneFk",
                    "([SeriesId] IS NOT NULL AND [ExceptionId] IS NULL) OR " +
                    "([SeriesId] IS NULL AND [ExceptionId] IS NOT NULL)");
            });

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

            builder.Property(a => a.SeriesId)
                   .IsRequired(false);

            builder.Property(a => a.ExceptionId)
                   .IsRequired(false);

            builder.Property(a => a.FileName)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskAttachment.MaxFileNameLength);

            builder.Property(a => a.ContentType)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskAttachment.MaxContentTypeLength);

            builder.Property(a => a.SizeBytes)
                   .IsRequired();

            builder.Property(a => a.BlobPath)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskAttachment.MaxBlobPathLength);

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
            // Foreign keys
            // -------------------------

            // SeriesId → RecurringTaskSeries.Id, Cascade
            // Physical series delete removes all template attachment rows.
            builder.HasOne<RecurringTaskSeries>()
                   .WithMany()
                   .HasForeignKey(a => a.SeriesId)
                   .OnDelete(DeleteBehavior.Cascade)
                   .IsRequired(false);

            // ExceptionId → RecurringTaskExceptions.Id, Restrict (NO ACTION)
            // SQL Server rejects multiple cascade paths from RecurringTaskSeries to
            // RecurringTaskAttachment (direct via SeriesId, and indirect via
            // RecurringTaskException.SeriesId). Application layer handles cascade via
            // SoftDeleteAllForExceptionAsync called before exception soft-delete.
            builder.HasOne<RecurringTaskException>()
                   .WithMany()
                   .HasForeignKey(a => a.ExceptionId)
                   .OnDelete(DeleteBehavior.Restrict)
                   .IsRequired(false);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(a => !a.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Load all active template attachments for a series
            //    Filtered to only rows where SeriesId IS NOT NULL to keep the index small.
            builder.HasIndex(a => a.SeriesId)
                   .HasFilter("[SeriesId] IS NOT NULL AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_RecurringTaskAttachments_SeriesId");

            // 2) Load all active exception attachment overrides for a specific exception.
            //    Filtered to only rows where ExceptionId IS NOT NULL.
            builder.HasIndex(a => a.ExceptionId)
                   .HasFilter("[ExceptionId] IS NOT NULL AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_RecurringTaskAttachments_ExceptionId");

            // 3) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(a => new { a.UserId, a.UpdatedAtUtc })
                   .HasDatabaseName("IX_RecurringTaskAttachments_UserId_UpdatedAtUtc");

            // 4) Blob path lookup for orphan-cleanup worker and shared-blob guard
            builder.HasIndex(a => a.BlobPath)
                   .HasDatabaseName("IX_RecurringTaskAttachments_BlobPath");
        }
    }
}
