using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the Note entity.
    /// Keeps database mapping explicit and optimized for calendar queries.
    /// </summary>
    public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
    {
        public void Configure(EntityTypeBuilder<Note> builder)
        {
            builder.ToTable("Notes");

            // Primary key
            builder.HasKey(n => n.Id);

            // -------------------------
            // Core domain properties
            // -------------------------

            builder.Property(n => n.UserId)
                   .IsRequired();

            builder.Property(n => n.Date)
                   .IsRequired();

            builder.Property(n => n.Title)
                   .IsRequired(false)
                   .HasMaxLength(Note.MaxTitleLength);

            builder.Property(n => n.Content)
                   .IsRequired(false);
            // If you want to cap the size, you can do:
            // .HasMaxLength(4000) or .HasColumnType("nvarchar(max)");

            // Versioning
            builder.Property(n => n.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Base Entity<T> properties
            // -------------------------

            builder.Property(n => n.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2"); 

            builder.Property(n => n.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2"); 

            builder.Property(n => n.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // Configure RowVersion as concurrency token (SQL Server rowversion)
            // This follows official EF Core guidance for optimistic concurrency. 
            builder.Property(n => n.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Global query filter
            // -------------------------

            // Hide soft-deleted notes from all normal queries by default.
            // This must match your overall soft-delete strategy and how you do it for TaskItem.
            builder.HasQueryFilter(n => !n.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // For "notes for day" queries in the calendar/day view.
            builder.HasIndex(n => new { n.UserId, n.Date });

            // For user-level scans
            builder.HasIndex(n => n.UserId);

            // For sync queries (GetChangedSinceAsync)
            builder.HasIndex(n => new { n.UserId, n.UpdatedAtUtc });
        }
    }
}
