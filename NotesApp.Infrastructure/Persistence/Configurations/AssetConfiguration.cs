using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the Asset entity.
    /// 
    /// Asset has a 1:1 relationship with Block (one block can have at most one asset).
    /// Assets are immutable after creation (no Version tracking needed).
    /// </summary>
    public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
    {
        public void Configure(EntityTypeBuilder<Asset> builder)
        {
            builder.ToTable("Assets");

            // Primary key
            builder.HasKey(a => a.Id);

            // -------------------------
            // Core properties
            // -------------------------

            builder.Property(a => a.UserId)
                   .IsRequired();

            builder.Property(a => a.BlockId)
                   .IsRequired();

            builder.Property(a => a.FileName)
                   .IsRequired()
                   .HasMaxLength(256);

            builder.Property(a => a.ContentType)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(a => a.SizeBytes)
                   .IsRequired();

            builder.Property(a => a.BlobPath)
                   .IsRequired()
                   .HasMaxLength(500);

            // -------------------------
            // Base Entity properties
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

            builder.Property(a => a.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Relationships
            // -------------------------

            // 1:1 relationship: Asset → Block
            // One block can have at most one asset.
            // Using HasOne().WithOne() pattern (no navigation property on Block).
            builder.HasOne<Block>()
                   .WithOne()
                   .HasForeignKey<Asset>(a => a.BlockId)
                   .OnDelete(DeleteBehavior.Cascade);

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(a => !a.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // Unique constraint: one asset per block (enforces 1:1 at DB level)
            builder.HasIndex(a => a.BlockId)
                   .IsUnique()
                   .HasDatabaseName("IX_Assets_Block");

            // User queries
            builder.HasIndex(a => a.UserId)
                   .HasDatabaseName("IX_Assets_User");

            // Blob path lookup (for cleanup jobs)
            builder.HasIndex(a => a.BlobPath)
                   .HasDatabaseName("IX_Assets_BlobPath");
        }
    }
}
