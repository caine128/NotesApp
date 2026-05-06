using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Entities;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the SyncSequenceStates table — per-user sequence allocator and
    /// retention watermark. Touched only by the SaveChanges interceptor (raw SQL with UPDLOCK)
    /// and by the retention sweep.
    /// </summary>
    public sealed class SyncSequenceStateConfiguration : IEntityTypeConfiguration<SyncSequenceState>
    {
        public void Configure(EntityTypeBuilder<SyncSequenceState> builder)
        {
            builder.ToTable("SyncSequenceStates");

            builder.HasKey(x => x.UserId);

            builder.Property(x => x.UserId)
                   .ValueGeneratedNever();

            builder.Property(x => x.NextSequence)
                   .IsRequired()
                   .HasDefaultValue(1L);

            builder.Property(x => x.MinRetainedSequence)
                   .IsRequired()
                   .HasDefaultValue(0L);
        }
    }
}
