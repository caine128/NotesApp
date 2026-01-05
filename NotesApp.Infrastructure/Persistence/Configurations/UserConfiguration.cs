using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// EF Core configuration for the Users table.
    /// </summary>
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            // Primary key
            builder.HasKey(u => u.Id);

            // Concurrency token (SQL Server rowversion)
            builder.Property(u => u.RowVersion)
                   .IsRowVersion();

            // Properties
            builder.Property(u => u.Email)
                   .IsRequired()
                   .HasMaxLength(User.MaxEmailLength);

            builder.Property(u => u.DisplayName)
                   .HasMaxLength(User.MaxDisplayNameLentgh);

            // Audit fields from base Entity
            builder.Property(u => u.CreatedAtUtc)
                   .IsRequired();

            builder.Property(u => u.UpdatedAtUtc)
                   .IsRequired();

            // Soft delete global filter
            builder.HasQueryFilter(u => !u.IsDeleted);

            // Index on Email for quick lookup (not unique because emails might change / be missing)
            builder.HasIndex(u => u.Email);
        }
    }
}
