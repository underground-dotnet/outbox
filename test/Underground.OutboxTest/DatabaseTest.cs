using Microsoft.Extensions.Logging;

using Underground.Outbox;

[assembly: CaptureConsole]

// Every test shares one Postgres instance and pg_snapshot_xmin is cluster-wide, so an open write
// transaction in one test withholds messages from every other test (ADR 0002). Stated here rather than
// only in xunit.runner.json, where a typo silently costs the whole file and with it this guarantee.
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
    public TestDbContext CreateDbContext(ProcessMessagesOnSaveChangesInterceptor? interceptor = null) =>
        Database.CreateDbContext(interceptor);

    /// <summary>Drops the test's database.</summary>
    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
