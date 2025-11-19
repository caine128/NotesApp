using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

            // Concurrency token, if your base Entity has RowVersion (byte[])
            // builder.Property(t => t.RowVersion)
            //        .IsRowVersion();

            // Properties
            builder.Property(t => t.UserId)
                   .IsRequired();

            builder.Property(t => t.Date)
                   .HasColumnType("date")
                   .IsRequired();

            builder.Property(t => t.Title)
                   .IsRequired()
                   .HasMaxLength(200); // sensible default, adjust as you like

            builder.Property(t => t.IsCompleted)
                   .IsRequired();

            builder.Property(t => t.ReminderAtUtc)
                   .HasColumnType("datetime2"); // or "timestamp" etc. for PostgreSQL; provider will adjust

            // Audit fields if present in base entity
            // builder.Property(t => t.CreatedAtUtc).IsRequired();
            // builder.Property(t => t.UpdatedAtUtc).IsRequired();

            // Soft-delete: filter out deleted tasks automatically
            builder.HasQueryFilter(t => !t.IsDeleted);

            // Indexes to optimize typical queries:
            // 1) Tasks for a given user and date (day view)
            builder.HasIndex(t => new { t.UserId, t.Date });

            // 2) Tasks per user (useful for month aggregates, etc.)
            builder.HasIndex(t => t.UserId);
        }
    }
}
