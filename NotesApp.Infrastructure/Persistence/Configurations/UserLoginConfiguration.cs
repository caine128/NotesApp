using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the UserLogins table.
    /// </summary>
    public sealed class UserLoginConfiguration : IEntityTypeConfiguration<UserLogin>
    {
        public void Configure(EntityTypeBuilder<UserLogin> builder)
        {
            builder.ToTable("UserLogins");

            // Primary key
            builder.HasKey(ul => ul.Id);

            // Concurrency token
            builder.Property(ul => ul.RowVersion)
                   .IsRowVersion();

            // Relationships
            builder.HasOne(ul => ul.User)
                   .WithMany(u => u.Logins)
                   .HasForeignKey(ul => ul.UserId)
                   .OnDelete(DeleteBehavior.Cascade);

            // Properties
            builder.Property(ul => ul.Provider)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(ul => ul.ExternalId)
                   .IsRequired()
                   .HasMaxLength(200);

            builder.Property(ul => ul.ProviderDisplayName)
                   .HasMaxLength(200);

            builder.Property(ul => ul.CreatedAtUtc)
                   .IsRequired();

            builder.Property(ul => ul.UpdatedAtUtc)
                   .IsRequired();

            // Soft delete filter
            builder.HasQueryFilter(ul => !ul.IsDeleted);

            // Critical: unique constraint to avoid race conditions in "GetOrCreateUser".
            builder.HasIndex(ul => new { ul.Provider, ul.ExternalId })
                   .IsUnique();
        }
    }
}
