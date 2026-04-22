using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the RecurringTaskSeries entity.
    ///
    /// Key decisions:
    /// - FK to RecurringTaskRoots uses Cascade — when a root is physically deleted (rare),
    ///   all its series segments are also removed.
    /// - FK to TaskCategories uses NoAction — same rationale as TaskItem.CategoryId.
    /// - MaterializedUpToDate column is indexed for the horizon worker polling query.
    /// - RRuleString stores only the FREQ/BYDAY/INTERVAL/COUNT body (no DTSTART/UNTIL).
    ///   DTSTART → StartsOnDate column; UNTIL → EndsBeforeDate column.
    /// - All template task fields mirror TaskItem's nullable/max-length settings.
    /// </summary>
    public sealed class RecurringTaskSeriesConfiguration : IEntityTypeConfiguration<RecurringTaskSeries>
    {
        public void Configure(EntityTypeBuilder<RecurringTaskSeries> builder)
        {
            builder.ToTable("RecurringTaskSeries");

            // Primary key
            builder.HasKey(s => s.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(s => s.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Identity properties
            // -------------------------

            builder.Property(s => s.UserId)
                   .IsRequired();

            builder.Property(s => s.RootId)
                   .IsRequired();

            // -------------------------
            // Recurrence rule
            // -------------------------

            builder.Property(s => s.RRuleString)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskSeries.MaxRRuleStringLength);

            builder.Property(s => s.StartsOnDate)
                   .HasColumnType("date")
                   .IsRequired();

            builder.Property(s => s.EndsBeforeDate)
                   .HasColumnType("date")
                   .IsRequired(false);

            // -------------------------
            // Template task fields
            // -------------------------

            builder.Property(s => s.Title)
                   .IsRequired()
                   .HasMaxLength(RecurringTaskSeries.MaxTitleLength);

            builder.Property(s => s.Description)
                   .IsRequired(false);

            builder.Property(s => s.StartTime)
                   .IsRequired(false);

            builder.Property(s => s.EndTime)
                   .IsRequired(false);

            builder.Property(s => s.Location)
                   .IsRequired(false);

            builder.Property(s => s.TravelTime)
                   .IsRequired(false);

            builder.Property(s => s.CategoryId)
                   .IsRequired(false);

            builder.Property(s => s.Priority)
                   .IsRequired()
                   .HasDefaultValue(TaskPriority.Normal)
                   .HasSentinel((TaskPriority)0);

            builder.Property(s => s.MeetingLink)
                   .IsRequired(false);

            builder.Property(s => s.ReminderOffsetMinutes)
                   .IsRequired(false);

            // -------------------------
            // Materialization tracking
            // -------------------------

            builder.Property(s => s.MaterializedUpToDate)
                   .HasColumnType("date")
                   .IsRequired();

            // -------------------------
            // Versioning
            // -------------------------

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

            // Root FK — Cascade: if the root is physically deleted all series are removed.
            builder.HasOne<RecurringTaskRoot>()
                   .WithMany()
                   .HasForeignKey(s => s.RootId)
                   .OnDelete(DeleteBehavior.Cascade);

            // Category FK — NoAction: same rationale as TaskItem.CategoryId.
            // Application layer nullifies CategoryId on affected series via UpdateTemplate().
            builder.HasOne<TaskCategory>()
                   .WithMany()
                   .HasForeignKey(s => s.CategoryId)
                   .OnDelete(DeleteBehavior.NoAction)
                   .IsRequired(false);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(s => !s.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) "Edit all" / "delete all": get all series for a root
            builder.HasIndex(s => new { s.UserId, s.RootId })
                   .HasDatabaseName("IX_RecurringTaskSeries_UserId_RootId");

            // 2) Horizon worker polling: find series that need materialization
            builder.HasIndex(s => s.MaterializedUpToDate)
                   .HasFilter("[IsDeleted] = 0")
                   .HasDatabaseName("IX_RecurringTaskSeries_MaterializedUpToDate");

            // 3) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(s => new { s.UserId, s.UpdatedAtUtc })
                   .HasDatabaseName("IX_RecurringTaskSeries_UserId_UpdatedAtUtc");
        }
    }
}
