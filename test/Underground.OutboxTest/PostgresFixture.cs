using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using Testcontainers.PostgreSql;
using Testcontainers.Xunit;

using Underground.OutboxTest;

using Xunit.Sdk;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Underground.OutboxTest;

/// <summary>
/// The single Postgres instance the whole test assembly shares, and the template database every test is
/// copied from.
/// </summary>
/// <remarks>
/// One instance rather than one per test, because booting a container dominated the suite. Tests cannot run
/// in parallel against it: <c>pg_snapshot_xmin</c> is cluster-wide, so an open write transaction in one test's
/// database withholds messages from every other database on the same instance.
/// </remarks>
public sealed class PostgresFixture(IMessageSink messageSink)
    : DbContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink)
{
    private const string TemplateDatabase = "outbox_template";

    private static PostgresFixture? s_current;

    private int _databaseCount;

    /// <inheritdoc />
    public override DbProviderFactory DbProviderFactory => NpgsqlFactory.Instance;

    internal static PostgresFixture Current =>
        s_current ?? throw new InvalidOperationException($"The {nameof(PostgresFixture)} has not been initialised.");

    /// <summary>Starts the instance and builds the template all tests are copied from.</summary>
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateTemplateAsync();
        s_current = this;
    }

    /// <inheritdoc />
    protected override PostgreSqlBuilder Configure() => new PostgreSqlBuilder("postgres:18.1");

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        s_current = null;
        await base.DisposeAsyncCore();
    }

    internal async Task<TestDatabase> CreateDatabaseAsync(ILoggerFactory loggerFactory)
    {
        var name = $"test_{Interlocked.Increment(ref _databaseCount)}";
        await ExecuteAsync($"""CREATE DATABASE "{name}" TEMPLATE "{TemplateDatabase}" """);

        return new TestDatabase(name, ConnectionStringFor(name), this, loggerFactory);
    }

    internal async Task DropDatabaseAsync(string database)
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE)""");
    }

    /// <summary>
    /// Fails the test that leaked rather than the test that suffers for it: a transaction left open holds
    /// <c>pg_snapshot_xmin</c> back, and every later test then finds its messages unstable and times out.
    /// </summary>
    internal async Task AssertNoOpenTransactionsAsync(string database)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT count(*) FROM pg_stat_activity WHERE datname = @database AND state = 'idle in transaction'";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "database";
        parameter.Value = database;
        command.Parameters.Add(parameter);

        var open = (long?)await command.ExecuteScalarAsync() ?? 0;
        if (open > 0)
        {
            throw new InvalidOperationException(
                $"The test left {open} transaction(s) open on {database}. Dispose the DbContext or stop the processor, "
                + "or every test after this one will see no claimable messages.");
        }
    }

    private async Task CreateTemplateAsync()
    {
        await ExecuteAsync($"""CREATE DATABASE "{TemplateDatabase}" """);

        // InboxOutboxDbContext rather than TestDbContext: its model is a superset, so one template serves both.
        var options = new DbContextOptionsBuilder<InboxOutboxDbContext>()
            .UseNpgsql(ConnectionStringFor(TemplateDatabase))
            .Options;

        await using (var context = new InboxOutboxDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        // Postgres refuses to copy a template that still has connections, and the pool outlives the context.
        NpgsqlConnection.ClearAllPools();
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
