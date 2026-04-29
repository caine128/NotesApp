# Infrastructure Testing Patterns

Patterns for resetting database state between tests using Respawn when containers are shared across a test collection.

## Contents

- [Database Reset with Respawn](#database-reset-with-respawn)

## Database Reset with Respawn

When reusing a container across tests, use [Respawn](https://github.com/jbogard/Respawn) to reset data between tests instead of recreating the container. Respawn issues targeted `DELETE` statements that respect foreign-key order, leaving the schema and migration history intact.

```xml
<PackageReference Include="Respawn" Version="*" />
```

### Fixture with Respawn

```csharp
using Respawn;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Apply EF Core migrations once for the whole fixture
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        _respawner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            TablesToIgnore = new Table[]
            {
                "__EFMigrationsHistory"
            },
            DbAdapter = DbAdapter.SqlServer
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await _respawner.ResetAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }
```

### Using Respawn in Tests

```csharp
[Collection("Database")]
public class NoteRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public NoteRepositoryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Clean slate before every test
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddNote_Persists()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);
        db.Notes.Add(new Note { Title = "Test" });
        await db.SaveChangesAsync();

        var count = await db.Notes.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task SecondTest_StartsWithCleanDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;

        await using var db = new AppDbContext(options);
        var count = await db.Notes.CountAsync();
        count.Should().Be(0); // Respawn reset between tests
    }
}
```

### Respawn Options

```csharp
_respawner = await Respawner.CreateAsync(connectionString, new RespawnerOptions
{
    TablesToIgnore = new Table[]
    {
        "__EFMigrationsHistory",        // never delete migration history
        new Table("dbo", "LookupData"), // static reference data
    },
    SchemasToInclude = new[] { "dbo" },
    DbAdapter = DbAdapter.SqlServer,
    WithReseed = true  // reset IDENTITY seeds
});
```

### Why Respawn Over Container Recreation

| Approach | Speed | Isolation | Notes |
|---|---|---|---|
| New container per test class | Slow (10–30 s) | Complete | Good for full integration suites |
| Respawn between tests | Fast (~50 ms) | Data only | Schema and migrations preserved |
| Transaction rollback | Fastest | Complete | Cannot test commit/rollback behavior itself |
