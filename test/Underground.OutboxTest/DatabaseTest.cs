using Microsoft.Extensions.Logging;

using Underground.Outbox;

[assembly: CaptureConsole]

// Every test shares one Postgres instance and pg_snapshot_xmin is cluster-wide, so an open write
// transaction in one test withholds messages from every other test (ADR 0002). Here rather than in
// xunit.runner.json, which the runner discards whole when it fails to parse - taking this with it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Underground.OutboxTest;

/// <summary>Gives every test its own database on the assembly's shared Postgres instance.</summary>
public partial class DatabaseTest : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the test's database.</summary>
    public DatabaseTest(ITestOutputHelper testOutputHelper)
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.ConfigureTestLogger(testOutputHelper));

        // Blocking, because xUnit constructs the test class before IAsyncLifetime runs and several tests build
        // their service provider in their own constructor. Copying the template takes milliseconds.
        Database = PostgresFixture.Current.CreateDatabaseAsync(_loggerFactory).GetAwaiter().GetResult();
    }

    /// <summary>The database this test owns.</summary>
    public TestDatabase Database { get; }

    /// <summary>Opens a context on this test's database. Also resolvable from the service provider.</summary>
    public TestDbContext CreateDbContext(ProcessMessagesOnSaveChangesInterceptor<TestDbContext>? interceptor = null) =>
        Database.CreateDbContext(interceptor);

    /// <summary>Drops the test's database, once whatever the test built has let go of it.</summary>
    public async ValueTask DisposeAsync()
    {
        await ReleaseOwnedResourcesAsync();
        await Database.DisposeAsync();
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Released before the database is dropped. A test that builds its own provider disposes it here: the
    /// connection pool outlives the contexts, and a pooled connection left open makes the drop fail.
    /// </summary>
    protected virtual ValueTask ReleaseOwnedResourcesAsync() => ValueTask.CompletedTask;
}
