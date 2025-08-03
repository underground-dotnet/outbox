
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.DatabaseProvider;

#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
public sealed class PostgresDatabaseProvider(
    OutboxServiceConfiguration config,
    IServiceScopeFactory scopeFactory,
    ILogger<PostgresDatabaseProvider> logger
) : IOutboxDatabaseProvider, IDisposable
{
    private readonly Type _dbContextType = config.DbContextType ?? throw new NoDbContextAssignedException();
    private readonly IServiceScope _scope = scopeFactory.CreateScope();

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Dispose()
    {
        _scope.Dispose();
    }

    public async Task<IEnumerable<string>> GetPartitionsAsync(CancellationToken cancellationToken)
    {
        using var dbContext = (DbContext)_scope.ServiceProvider.GetRequiredService(_dbContextType);
        return await dbContext.Database
            .SqlQueryRaw<string>($"""SELECT DISTINCT(partition_key) FROM {config.FullTableName} WHERE completed = false""")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task FetchAndUpdateMessagesWithTransactionAsync(Func<IEnumerable<OutboxMessage>, Task<IEnumerable<int>>> action, int batchSize, CancellationToken cancellationToken)
    {
        using var dbContext = (DbContext)_scope.ServiceProvider.GetRequiredService(_dbContextType);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw query is needed to inject tablename
        var batchSizeValue = new NpgsqlParameter("@batchSize", batchSize);

        // use NOWAIT instead of SKIP LOCKED to avoid deadlocks when multiple instances are running and to keep order guaranteed
        var messages = await dbContext.Database.SqlQueryRaw<OutboxMessage>(
            $"""SELECT * FROM {config.FullTableName} WHERE completed = false ORDER BY "id" FOR UPDATE NOWAIT LIMIT @batchSize""", batchSizeValue
        )
        .AsNoTracking()
        .ToListAsync(cancellationToken);

        var successIds = await action.Invoke(messages);

        // mark as processed
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""UPDATE {config.FullTableName} SET completed = true WHERE "id" = ANY(@ids)""",
            new NpgsqlParameter("@ids", successIds.ToArray())
        );
        await transaction.CommitAsync(cancellationToken);
    }
}
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.
