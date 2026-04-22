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
        public DbSet<Block> Blocks => Set<Block>();
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<UserDevice> UserDevices { get; set; } = null!;
        // REFACTORED: added TaskCategories for task categories feature
        public DbSet<TaskCategory> TaskCategories => Set<TaskCategory>();
        // REFACTORED: added Subtasks for subtasks feature
        public DbSet<Subtask> Subtasks => Set<Subtask>();
        // REFACTORED: added Attachments for task-attachments feature
        public DbSet<Attachment> Attachments => Set<Attachment>();
        // REFACTORED: added recurring-task DbSets for recurring-tasks feature
        public DbSet<RecurringTaskRoot> RecurringTaskRoots => Set<RecurringTaskRoot>();
        public DbSet<RecurringTaskSeries> RecurringTaskSeries => Set<RecurringTaskSeries>();
        public DbSet<RecurringTaskSubtask> RecurringTaskSubtasks => Set<RecurringTaskSubtask>();
        public DbSet<RecurringTaskException> RecurringTaskExceptions => Set<RecurringTaskException>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ Apply all IEntityTypeConfiguration<T> classes in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
