using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NotesApp.Infrastructure.Persistence;
using NotesApp.Infrastructure.Persistence.Interceptors;
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
            // The SyncChangeSequenceInterceptor matches production DI: every DbContext has it.
            // It is a no-op when no SyncChange entries are pending in the change tracker, and is
            // required for any test that triggers SyncChange writes via SyncPushCommandHandler /
            // non-sync command handlers / etc., to avoid (UserId, Sequence)=(_,0) unique violations.
            return CreateContextCore(includeSyncInterceptor: true);
        }

        /// <summary>
        /// Alias retained for clarity in interceptor-focused tests. Same behavior as
        /// <see cref="CreateContext"/>.
        /// </summary>
        public static AppDbContext CreateContextWithSyncInterceptor()
        {
            return CreateContextCore(includeSyncInterceptor: true);
        }

        /// <summary>
        /// Like CreateContextWithSyncInterceptor but does NOT drop/recreate the database.
        /// Use when a test needs multiple contexts sharing the same already-seeded database
        /// (e.g. concurrent-writer tests, retry-replay tests, multi-step verification).
        /// </summary>
        public static AppDbContext CreateContextWithSyncInterceptorReuseDb()
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString);
            builder.AddInterceptors(new SyncChangeSequenceInterceptor());
            return new AppDbContext(builder.Options);
        }

        private static AppDbContext CreateContextCore(bool includeSyncInterceptor)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString);

            if (includeSyncInterceptor)
            {
                builder.AddInterceptors(new SyncChangeSequenceInterceptor());
            }

            var context = new AppDbContext(builder.Options);

            // Release any pooled connections from the previous test before issuing
            // DROP/CREATE — otherwise a lingering pooled connection to NotesApp_Tests
            // can cause EnsureDeleted to silently no-op or EnsureCreated to fail with
            // "Database already exists" / "file in use".
            SqlConnection.ClearAllPools();

            bool dbExisted = context.Database.EnsureDeleted();
            if (!dbExisted)
            {
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
