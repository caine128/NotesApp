using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the RecurringTaskSubtask entity.
    ///
    /// RecurringTaskSubtask serves dual purpose via two nullable FKs:
    /// - SeriesId set, ExceptionId null  → series template subtask
    /// - ExceptionId set, SeriesId null  → exception subtask override
    ///
    /// Exactly one FK must be non-null. This is enforced by:
    /// 1. Two factory methods (CreateForSeries / CreateForException) in the domain entity
    /// 2. A DB-level check constraint added here
    ///
    /// No EF navigation properties — subtasks are loaded via separate IRecurringTaskSubtaskRepository
    /// calls (GetBySeriesIdAsync / GetByExceptionIdAsync), consistent with existing conventions.
    /// </summary>
    public sealed class RecurringTaskSubtaskConfiguration : IEntityTypeConfiguration<RecurringTaskSubtask>
    {
        public void Configure(EntityTypeBuilder<RecurringTaskSubtask> builder)
        {
            builder.ToTable("RecurringTaskSubtasks", t =>
            {
                // Enforce the dual-FK invariant at the DB level:
                // exactly one of (SeriesId, ExceptionId) must be non-null.
                t.HasCheckConstraint(
                    "CK_RecurringTaskSubtasks_ExactlyOneFk",
                    "([SeriesId] IS NOT NULL AND [ExceptionId] IS NULL) OR " +
                    "([SeriesId] IS NULL AND [ExceptionId] IS NOT NULL)");
            });

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

            builder.Property(s => s.SeriesId)
                   .IsRequired(false);

            builder.Property(s => s.ExceptionId)
                   .IsRequired(false);

            builder.Property(s => s.Text)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskSubtask.MaxTextLength);

            builder.Property(s => s.IsCompleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            builder.Property(s => s.Position)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskSubtask.MaxPositionLength);

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
            // Foreign keys
            // -------------------------

            // SeriesId → RecurringTaskSeries.Id, Cascade
            // When a series is physically deleted, its template subtasks are also removed.
            builder.HasOne<RecurringTaskSeries>()
                   .WithMany()
                   .HasForeignKey(s => s.SeriesId)
                   .OnDelete(DeleteBehavior.Cascade)
                   .IsRequired(false);

            // ExceptionId → RecurringTaskExceptions.Id, Restrict (NO ACTION)
            // Cannot use Cascade here: SQL Server rejects multiple cascade paths from
            // RecurringTaskSeries → RecurringTaskSubtask (direct via SeriesId, and indirect via
            // RecurringTaskException.SeriesId → RecurringTaskSeries). The app exclusively uses
            // soft-delete, so physical deletes never occur in practice — Restrict is safe.
            builder.HasOne<RecurringTaskException>()
                   .WithMany()
                   .HasForeignKey(s => s.ExceptionId)
                   .OnDelete(DeleteBehavior.Restrict)
                   .IsRequired(false);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(s => !s.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Load all active template subtasks for a series (materializer, GetVirtualOccurrenceDetail)
            //    Filtered to only rows where SeriesId IS NOT NULL to keep the index small.
            builder.HasIndex(s => s.SeriesId)
                   .HasFilter("[SeriesId] IS NOT NULL AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_RecurringTaskSubtasks_SeriesId");

            // 2) Load all active exception subtask overrides for a specific exception
            //    Filtered to only rows where ExceptionId IS NOT NULL.
            builder.HasIndex(s => s.ExceptionId)
                   .HasFilter("[ExceptionId] IS NOT NULL AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_RecurringTaskSubtasks_ExceptionId");

            // 3) Sync pull: GetChangedSinceAsync covers both series and exception subtask rows
            builder.HasIndex(s => new { s.UserId, s.UpdatedAtUtc })
                   .HasDatabaseName("IX_RecurringTaskSubtasks_UserId_UpdatedAtUtc");
        }
    }
}
