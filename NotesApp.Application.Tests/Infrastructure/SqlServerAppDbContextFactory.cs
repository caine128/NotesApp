using Microsoft.EntityFrameworkCore;
using NotesApp.Infrastructure.Persistence;
using System;
using System.IO;

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

            // Drop the database if it is registered in the LocalDB catalog.
            // EnsureDeleted() returns true  → SQL Server dropped the DB and released the files.
            // EnsureDeleted() returns false → DB was not in the catalog; physical MDF/LDF files
            //   may still exist from a previous run that crashed while the instance was down.
            //   In that case we must delete the files manually before EnsureCreated() runs,
            //   otherwise SQL Server refuses: "Cannot create file … because it already exists."
            bool dbExisted = context.Database.EnsureDeleted();
            if (!dbExisted)
            {
                // Only safe to touch the files when SQL Server has no handle on them.
                DeleteOrphanedDbFiles();
            }

            context.Database.EnsureCreated();

            return context;
        }

        private static void DeleteOrphanedDbFiles()
        {
            // MSSQLLocalDB stores database files in the current user's profile directory.
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var fileName in new[] { "NotesApp_Tests.mdf", "NotesApp_Tests_log.ldf" })
            {
                var path = Path.Combine(userProfile, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
