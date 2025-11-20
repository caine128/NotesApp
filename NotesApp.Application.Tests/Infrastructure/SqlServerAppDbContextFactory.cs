using Microsoft.EntityFrameworkCore;
using NotesApp.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Application.Tests.Infrastructure
{
    /// <summary>
    /// Factory for creating an AppDbContext that uses a real SQL Server LocalDB database.
    /// This is used in tests to avoid SQLite and to get behaviour identical to production.
    /// </summary>
    public static class SqlServerAppDbContextFactory
    {
        // NOTE: Adjust instance name if your LocalDB instance is different.
        private const string ConnectionString =
            "Server=(localdb)\\MSSQLLocalDB;" +
            "Database=NotesApp_Tests;" +
            "Trusted_Connection=True;" +
            "MultipleActiveResultSets=true;" +
            "TrustServerCertificate=True;";

        /// <summary>
        /// Creates a new AppDbContext backed by a clean LocalDB database.
        /// The database is deleted and recreated for each call, so every test
        /// starts from an empty schema.
        /// </summary>
        public static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            var context = new AppDbContext(options);

            // Clean database for each test run
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();

            return context;
        }
    }
}
