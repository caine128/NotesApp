using Microsoft.EntityFrameworkCore;
using NotesApp.Domain.Entities;
using NotesApp.Domain.Users;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace NotesApp.Infrastructure.Persistence
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        // DbSets = tables in the database
        public DbSet<User> Users => Set<User>();
        public DbSet<UserLogin> UserLogins => Set<UserLogin>();
        public DbSet<TaskItem> Tasks => Set<TaskItem>();
        public DbSet<Note> Notes => Set<Note>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ Apply all IEntityTypeConfiguration<T> classes in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
