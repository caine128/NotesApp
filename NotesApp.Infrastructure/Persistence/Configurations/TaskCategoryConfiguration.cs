using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the TaskCategory entity.
    ///
    /// Key decisions:
    /// - Global query filter hides soft-deleted categories from all normal queries.
    /// - Two composite indexes optimize per-user list queries and sync pull queries.
    /// - No FK constraint declared here — the FK from TaskItem.CategoryId is configured
    ///   in <see cref="TaskItemConfiguration"/> with DeleteBehavior.NoAction.
    /// </summary>
    public sealed class TaskCategoryConfiguration : IEntityTypeConfiguration<TaskCategory>
    {
        public void Configure(EntityTypeBuilder<TaskCategory> builder)
        {
            builder.ToTable("TaskCategories");

            // Primary key
            builder.HasKey(c => c.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(c => c.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Core domain properties
            // -------------------------

            builder.Property(c => c.UserId)
                   .IsRequired();

            builder.Property(c => c.Name)
                   .IsRequired()
                   .HasMaxLength(TaskCategory.MaxNameLength);

            // Version starts at 1 — mirrors TaskItem and Note configuration
            builder.Property(c => c.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(c => c.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(c => c.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(c => c.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Global query filter
            // -------------------------

            // Hide soft-deleted categories from all normal queries by default.
            // GetChangedSinceAsync uses IgnoreQueryFilters() to surface deleted
            // categories in the sync-deleted bucket.
            builder.HasQueryFilter(c => !c.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Fast per-user list queries (GET /api/categories)
            builder.HasIndex(c => c.UserId)
                   .HasDatabaseName("IX_TaskCategories_UserId");

            // 2) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(c => new { c.UserId, c.UpdatedAtUtc })
                   .HasDatabaseName("IX_TaskCategories_UserId_UpdatedAtUtc");
        }
    }
}
