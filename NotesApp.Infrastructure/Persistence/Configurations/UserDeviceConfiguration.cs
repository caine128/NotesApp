using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the UserDevices table.
    /// </summary>
    public sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
    {
        public void Configure(EntityTypeBuilder<UserDevice> builder)
        {
            builder.ToTable("UserDevices");

            builder.HasKey(d => d.Id);

            builder.Property(d => d.UserId)
                   .IsRequired();

            builder.Property(d => d.DeviceToken)
                   .IsRequired()
                   .HasMaxLength(UserDevice.MaxDeviceTokenLength);

            builder.Property(d => d.Platform)
                   .IsRequired()
                   .HasConversion<string>()   // or .HasConversion<int>() if you prefer
                   .HasMaxLength(UserDevice.MaxPlatformLength);

            builder.Property(d => d.DeviceName)
                   .HasMaxLength(UserDevice.MaxDeviceNameLength);

            builder.Property(d => d.LastSeenAtUtc)
                   .IsRequired();

            builder.Property(d => d.IsActive)
                   .IsRequired();

            builder.Property(d => d.CreatedAtUtc)
                   .IsRequired();

            builder.Property(d => d.UpdatedAtUtc)
                   .IsRequired();

            // Soft delete global filter (consistent with User, Note, Task)
            builder.HasQueryFilter(d => !d.IsDeleted);

            // Relationships
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(d => d.UserId)
                   .OnDelete(DeleteBehavior.Cascade);

            // Index for quick token lookup
            builder.HasIndex(d => d.DeviceToken);

            // Index for queries by user (often combined with IsActive)
            builder.HasIndex(d => new { d.UserId, d.IsActive });

            // Concurrency token (if your base Entity already configures this, you can remove)
            builder.Property(d => d.RowVersion)
                .IsRowVersion();
        }
    }
}
