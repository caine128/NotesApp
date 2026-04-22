using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the RecurringTaskRoot entity.
    ///
    /// RecurringTaskRoot is a thin identity anchor — it holds no recurrence rule data.
    /// Its sole purpose is to give all series segments (created by ThisAndFollowing splits)
    /// a stable, shared identity for "edit all" / "delete all" operations.
    /// </summary>
    public sealed class RecurringTaskRootConfiguration : IEntityTypeConfiguration<RecurringTaskRoot>
    {
        public void Configure(EntityTypeBuilder<RecurringTaskRoot> builder)
        {
            builder.ToTable("RecurringTaskRoots");

            // Primary key
            builder.HasKey(r => r.Id);

            // Concurrency token from base Entity<TId>
            builder.Property(r => r.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Core properties
            // -------------------------

            builder.Property(r => r.UserId)
                   .IsRequired();

            builder.Property(r => r.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Audit fields from base entity
            // -------------------------

            builder.Property(r => r.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(r => r.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(r => r.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(r => !r.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // 1) Tenant isolation + basic lookup
            builder.HasIndex(r => r.UserId)
                   .HasDatabaseName("IX_RecurringTaskRoots_UserId");

            // 2) Sync pull: GetChangedSinceAsync filters by UserId + UpdatedAtUtc
            builder.HasIndex(r => new { r.UserId, r.UpdatedAtUtc })
                   .HasDatabaseName("IX_RecurringTaskRoots_UserId_UpdatedAtUtc");
        }
    }
}
