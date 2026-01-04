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
    /// EF Core configuration for the Block entity.
    /// 
    /// Note: Block has a polymorphic parent (Note or Task), so we do NOT
    /// configure a foreign key constraint. The application layer is responsible
    /// for ensuring referential integrity.
    /// </summary>
    public sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
    {
        public void Configure(EntityTypeBuilder<Block> builder)
        {
            builder.ToTable("Blocks");

            // Primary key
            builder.HasKey(b => b.Id);

            // -------------------------
            // Core properties
            // -------------------------

            builder.Property(b => b.UserId)
                   .IsRequired();

            builder.Property(b => b.ParentId)
                   .IsRequired();

            builder.Property(b => b.ParentType)
                   .IsRequired()
                   .HasConversion<int>();

            builder.Property(b => b.Type)
                   .IsRequired()
                   .HasConversion<int>();

            builder.Property(b => b.Position)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(b => b.TextContent)
                   .IsRequired(false);
            // Text content can be large, use nvarchar(max)

            // -------------------------
            // Asset properties
            // -------------------------

            builder.Property(b => b.AssetId)
                   .IsRequired(false);

            builder.Property(b => b.AssetClientId)
                   .IsRequired(false)
                   .HasMaxLength(100);

            builder.Property(b => b.AssetFileName)
                   .IsRequired(false)
                   .HasMaxLength(256);

            builder.Property(b => b.AssetContentType)
                   .IsRequired(false)
                   .HasMaxLength(100);

            builder.Property(b => b.AssetSizeBytes)
                   .IsRequired(false);

            builder.Property(b => b.UploadStatus)
                   .IsRequired()
                   .HasConversion<int>()
                   .HasDefaultValue(UploadStatus.NotApplicable);

            // -------------------------
            // Versioning
            // -------------------------

            builder.Property(b => b.Version)
                   .IsRequired()
                   .HasDefaultValue(1L);

            // -------------------------
            // Base Entity properties
            // -------------------------

            builder.Property(b => b.CreatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(b => b.UpdatedAtUtc)
                   .IsRequired()
                   .HasColumnType("datetime2");

            builder.Property(b => b.IsDeleted)
                   .IsRequired()
                   .HasDefaultValue(false);

            builder.Property(b => b.RowVersion)
                   .IsRowVersion();

            // -------------------------
            // Relationships
            // -------------------------

            // NOTE: We do NOT configure HasOne<Note>() or HasOne<TaskItem>()
            // because Block has a polymorphic parent (ParentType determines
            // whether ParentId refers to a Note or Task).
            // EF Core does not support conditional foreign keys.
            // Application layer ensures referential integrity.

            // -------------------------
            // Global query filter
            // -------------------------

            builder.HasQueryFilter(b => !b.IsDeleted);

            // -------------------------
            // Indexes
            // -------------------------

            // Primary query: get blocks for a parent, ordered by position
            builder.HasIndex(b => new { b.ParentId, b.ParentType, b.Position })
                   .HasDatabaseName("IX_Blocks_Parent_Position");

            // Sync queries: get blocks changed since timestamp for a user
            builder.HasIndex(b => new { b.UserId, b.UpdatedAtUtc })
                   .HasDatabaseName("IX_Blocks_User_Updated");

            // Asset lookup: find block by asset ID
            builder.HasIndex(b => b.AssetId)
                   .HasDatabaseName("IX_Blocks_Asset")
                   .HasFilter("[AssetId] IS NOT NULL");

            // User authorization: all blocks for a user
            builder.HasIndex(b => b.UserId)
                   .HasDatabaseName("IX_Blocks_User");
        }
    }
}
