using Microsoft.Extensions.Logging;

using Underground.Outbox;

namespace Underground.OutboxTest;

/// <summary>
/// The database one test owns: copied from the template when the test starts, dropped when it ends.
/// </summary>
public sealed class TestDatabase(string name, string connectionString, PostgresFixture fixture, ILoggerFactory loggerFactory)
    : IAsyncDisposable
{
    /// <summary>Connects to this test's database, not to the instance's default one.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Opens a context on this database. The schema is already there, copied from the template.</summary>
    public TestDbContext CreateDbContext(ProcessMessagesOnSaveChangesInterceptor<TestDbContext>? interceptor = null) =>
        new(this, loggerFactory, interceptor);

    /// <summary>Checks the test left no transaction open, then drops the database.</summary>
    public async ValueTask DisposeAsync()
    {
        await fixture.AssertNoOpenTransactionsAsync(name);
        await fixture.DropDatabaseAsync(name);
    }
}
