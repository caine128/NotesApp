using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Common;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
    {
        public void Configure(EntityTypeBuilder<TaskItem> builder)
        {
            builder.ToTable("Tasks");

            // Primary key
            builder.HasKey(t => t.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(t => t.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Core properties
            // -------------------------


            builder.Property(t => t.UserId)
                   .IsRequired();

            builder.Property(t => t.Date)
                   .HasColumnType("date")
                   .IsRequired();

            builder.Property(t => t.Title)
                   .IsRequired()
                   .HasMaxLength(TaskItem.MaxTitleLength); // sensible default, adjust as you like

            builder.Property(t => t.IsCompleted)
                   .IsRequired();

            builder.Property(t => t.ReminderAtUtc)
                   .HasColumnType("datetime2"); // or "timestamp" etc. for PostgreSQL; provider will adjust

            // Versioning
            builder.Property(t => t.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // Reminder tracking fields
            builder.Property(t => t.ReminderAcknowledgedAtUtc)
                   .HasColumnType("datetime2");

            builder.Property(t => t.ReminderSentAtUtc)
                   .HasColumnType("datetime2");

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(t => t.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(t => t.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(t => t.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // Soft-delete: filter out deleted tasks automatically
            builder.HasQueryFilter(t => !t.IsDeleted);


            // -------------------------
            // Indexes
            // -------------------------

            // 1) Tasks for a given user and date (day view)
            builder.HasIndex(t => new { t.UserId, t.Date });

            // 2) Tasks per user (useful for month aggregates, etc.)
            builder.HasIndex(t => t.UserId);

            // 3) Sync-optimized index: user + UpdatedAtUtc
            builder.HasIndex(t => new { t.UserId, t.UpdatedAtUtc });

            // 4) Filtered index for overdue reminders (for ReminderMonitor worker)
            builder.HasIndex(
                    t => new
                    {
                        t.UserId,
                        t.ReminderAtUtc,
                        t.ReminderAcknowledgedAtUtc,
                        t.ReminderSentAtUtc
                    })
                   .HasDatabaseName("IX_Tasks_OverdueReminders")
                   .HasFilter("[ReminderAtUtc] IS NOT NULL " +
                              "AND [ReminderAcknowledgedAtUtc] IS NULL " +
                              "AND [ReminderSentAtUtc] IS NULL " +
                              "AND [IsDeleted] = 0");

            // REFACTORED: added Priority for task priority feature
            builder.Property(t => t.Priority)
                   .IsRequired()
                   .HasDefaultValue(TaskPriority.Normal)
                   .HasSentinel((TaskPriority)0); // 0 is intentionally unmapped; tells EF to always persist explicit values

            // REFACTORED: CategoryId FK + index for task categories feature

            // Nullable FK — a task may have at most one optional category.
            builder.Property(t => t.CategoryId)
                   .IsRequired(false);

            // DeleteBehavior.NoAction: no DB-level cascade.
            // - REST/web-client path: ClearCategoryFromTasksAsync bulk-nullifies CategoryId
            //   after soft-deleting the category.
            // - Sync push path: the mobile client sends affected task updates itself;
            //   the server does not cascade.
            builder.HasOne<TaskCategory>()
                   .WithMany()
                   .HasForeignKey(t => t.CategoryId)
                   .OnDelete(DeleteBehavior.NoAction)
                   .IsRequired(false);

            // 5) Filtered composite index for "tasks by category" queries.
            //    The filter excludes nulls and soft-deleted rows to keep the index small.
            builder.HasIndex(t => new { t.UserId, t.CategoryId })
                   .HasFilter("[CategoryId] IS NOT NULL AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_Tasks_UserCategory");
        }
    }
}
