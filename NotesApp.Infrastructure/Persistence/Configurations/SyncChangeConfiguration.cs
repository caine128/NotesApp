using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the SyncChanges feed table.
    /// Append-only history; no soft delete (retention sweep hard-deletes), no row version.
    /// Sequence is assigned by SyncChangeSequenceInterceptor at flush time, not by IDENTITY.
    /// </summary>
    public sealed class SyncChangeConfiguration : IEntityTypeConfiguration<SyncChange>
    {
        public void Configure(EntityTypeBuilder<SyncChange> builder)
        {
            builder.ToTable("SyncChanges");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                   .ValueGeneratedNever();

            builder.Property(x => x.UserId)
                   .IsRequired();

            builder.Property(x => x.Sequence)
                   .ValueGeneratedNever()
                   .IsRequired();

            builder.Property(x => x.EntityFamily)
                   .HasConversion<byte>()
                   .IsRequired();

            builder.Property(x => x.EntityId)
                   .IsRequired();

            builder.Property(x => x.Operation)
                   .HasConversion<byte>()
                   .IsRequired();

            builder.Property(x => x.ChangedAtUtc)
                   .HasColumnType("datetime2")
                   .IsRequired();

            builder.Property(x => x.OriginDeviceId);

            builder.Property(x => x.PayloadJson)
                   .HasColumnType("nvarchar(max)")
                   .IsRequired();

            builder.HasIndex(x => new { x.UserId, x.Sequence })
                   .IsUnique()
                   .HasDatabaseName("IX_SyncChanges_UserId_Sequence");
        }
    }
}
