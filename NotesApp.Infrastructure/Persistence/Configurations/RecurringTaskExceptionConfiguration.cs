using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the RecurringTaskException entity.
    ///
    /// Key decisions:
    /// - Unique filtered index on (SeriesId, OccurrenceDate) WHERE IsDeleted=0 ensures one
    ///   active exception per occurrence. The application layer also pre-checks before creating.
    /// - FK to TaskItems uses SetNull so that soft-deleting a TaskItem automatically clears
    ///   MaterializedTaskItemId without requiring a separate application-layer step.
    /// - FK to TaskCategories uses NoAction — same rationale as TaskItem.CategoryId.
    /// - No EF navigation properties: exception subtasks are loaded via separate
    ///   IRecurringTaskSubtaskRepository calls (consistent with existing conventions).
    /// - All override fields are nullable — null means "inherit from the series template".
    /// </summary>
    public sealed class RecurringTaskExceptionConfiguration : IEntityTypeConfiguration<RecurringTaskException>
    {
        public void Configure(EntityTypeBuilder<RecurringTaskException> builder)
        {
            builder.ToTable("RecurringTaskExceptions");

            // Primary key
            builder.HasKey(e => e.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(e => e.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Identity properties
            // -------------------------

            builder.Property(e => e.UserId)
                   .IsRequired();

            builder.Property(e => e.SeriesId)
                   .IsRequired();

            builder.Property(e => e.OccurrenceDate)
                   .HasColumnType("date")
                   .IsRequired();

            // -------------------------
            // Type flag
            // -------------------------

            builder.Property(e => e.IsDeletion)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Override fields (all nullable)
            // -------------------------

            builder.Property(e => e.OverrideTitle)
                   .IsRequired(false)
                   .HasMaxLength(TaskItem.MaxTitleLength);

            builder.Property(e => e.OverrideDescription)
                   .IsRequired(false);

            builder.Property(e => e.OverrideDate)
                   .HasColumnType("date")
                   .IsRequired(false);

            builder.Property(e => e.OverrideStartTime)
                   .IsRequired(false);

            builder.Property(e => e.OverrideEndTime)
                   .IsRequired(false);

            builder.Property(e => e.OverrideLocation)
                   .IsRequired(false);

            builder.Property(e => e.OverrideTravelTime)
                   .IsRequired(false);

            builder.Property(e => e.OverrideCategoryId)
                   .IsRequired(false);

            builder.Property(e => e.OverridePriority)
                   .IsRequired(false);

            builder.Property(e => e.OverrideMeetingLink)
                   .IsRequired(false);

            builder.Property(e => e.OverrideReminderAtUtc)
                   .HasColumnType("datetime2")
                   .IsRequired(false);

            // IsCompleted is stored per-occurrence on the exception (not inherited from the series
            // template — the series has no completion state). Defaults to false.
            builder.Property(e => e.IsCompleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // REFACTORED: added HasAttachmentOverride for recurring-task-attachments feature
            // Distinguishes "exception for another reason (e.g. title override)" from "exception whose
            // attachment list has been explicitly managed". Prevents snap-back to series attachments
            // when the last exception attachment is deleted (once set, never cleared automatically).
            builder.Property(e => e.HasAttachmentOverride)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Materialization link
            // -------------------------

            builder.Property(e => e.MaterializedTaskItemId)
                   .IsRequired(false);

            // -------------------------
            // Versioning
            // -------------------------

            builder.Property(e => e.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(e => e.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(e => e.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(e => e.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Foreign keys
            // -------------------------

            // SeriesId → RecurringTaskSeries.Id, Cascade
            // When a series segment is physically deleted, its exceptions are also removed.
            builder.HasOne<RecurringTaskSeries>()
                   .WithMany()
                   .HasForeignKey(e => e.SeriesId)
                   .OnDelete(DeleteBehavior.Cascade);

            // MaterializedTaskItemId → Tasks.Id, SetNull
            // When the linked TaskItem is (physically) deleted, the FK is automatically set to null.
            // This means the exception acts as a permanent virtual tombstone for the occurrence.
            builder.HasOne<TaskItem>()
                   .WithMany()
                   .HasForeignKey(e => e.MaterializedTaskItemId)
                   .OnDelete(DeleteBehavior.SetNull)
                   .IsRequired(false);

            // OverrideCategoryId → TaskCategories.Id, NoAction
            // Application layer nullifies OverrideCategoryId via UpdateOverride() when needed.
            builder.HasOne<TaskCategory>()
                   .WithMany()
                   .HasForeignKey(e => e.OverrideCategoryId)
                   .OnDelete(DeleteBehavior.NoAction)
                   .IsRequired(false);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(e => !e.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Unique constraint: one active exception per (SeriesId, OccurrenceDate).
            //    FILTERED to exclude soft-deleted rows — allows re-creation after soft-delete.
            builder.HasIndex(e => new { e.SeriesId, e.OccurrenceDate })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0")
                   .HasDatabaseName("UX_RecurringTaskExceptions_Series_OccurrenceDate");

            // 2) "Edit all" reverse lookup: find the exception for a given materialized TaskItem.
            //    Filtered to non-null values only to keep the index small.
            builder.HasIndex(e => e.MaterializedTaskItemId)
                   .HasFilter("[MaterializedTaskItemId] IS NOT NULL")
                   .HasDatabaseName("IX_RecurringTaskExceptions_MaterializedTaskItemId");

            // 3) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(e => new { e.UserId, e.UpdatedAtUtc })
                   .HasDatabaseName("IX_RecurringTaskExceptions_UserId_UpdatedAtUtc");
        }
    }
}
