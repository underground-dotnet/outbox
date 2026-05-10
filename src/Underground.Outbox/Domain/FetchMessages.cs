using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using System.Runtime.CompilerServices;

using Npgsql;

using Underground.Outbox.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Domain;

internal abstract partial class FetchMessages<TEntity>(IDbContext dbContext, ILogger<FetchMessages<TEntity>> logger) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = [];
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    internal async Task<List<TEntity>> ExecuteAsync(string partition, int batchSize, CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetValue(dbContext.Model, static model => BuildSql(model));

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("partition", partition));
                cmd.Parameters.Add(new NpgsqlParameter("batchSize", batchSize));

                var result = new List<TEntity>();

                LogFetchSql(partition, sql);
                var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        result.Add(BuildEntityFromReader(reader));
                    }
                }

                return result;
            }
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "55P03", StringComparison.Ordinal)) // lock_not_available
        {
            // another processor is already handling messages for this partition
            LogCouldNotAcquireLock(typeof(TEntity).Name, partition, ex);
            return [];
        }
    }

    private static string BuildSql(IModel model)
    {
        // dynamically extract table and column names to build the SQL query, since those can be overriden via EF Core mappings
        var entityType = model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException($"Entity type {typeof(TEntity)} not found in DbContext model.");
        var tableName = entityType.GetTableName() ?? throw new InvalidOperationException($"Table name for entity type {typeof(TEntity)} is not configured.");
        var schema = entityType.GetSchema();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";
        var tableIdentifier = StoreObjectIdentifier.Table(tableName, schema);

        var idColumn = entityType.FindProperty(nameof(IMessage.Id))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Id)} not found in entity type {typeof(TEntity)}.");
        var eventIdColumn = entityType.FindProperty(nameof(IMessage.EventId))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.EventId)} not found in entity type {typeof(TEntity)}.");
        var createdAtColumn = entityType.FindProperty(nameof(IMessage.CreatedAt))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.CreatedAt)} not found in entity type {typeof(TEntity)}.");
        var typeColumn = entityType.FindProperty(nameof(IMessage.Type))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Type)} not found in entity type {typeof(TEntity)}.");
        var partitionKeyColumn = entityType.FindProperty(nameof(IMessage.PartitionKey))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.PartitionKey)} not found in entity type {typeof(TEntity)}.");
        var dataColumn = entityType.FindProperty(nameof(IMessage.Data))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Data)} not found in entity type {typeof(TEntity)}.");
        var retryCountColumn = entityType.FindProperty(nameof(IMessage.RetryCount))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.RetryCount)} not found in entity type {typeof(TEntity)}.");
        var processedAtColumn = entityType.FindProperty(nameof(IMessage.ProcessedAt))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.ProcessedAt)} not found in entity type {typeof(TEntity)}.");

        return $"""
            SELECT "{idColumn}", "{eventIdColumn}", "{createdAtColumn}", "{typeColumn}", "{partitionKeyColumn}", "{dataColumn}", "{retryCountColumn}", "{processedAtColumn}"
            FROM {fullTableName}
            WHERE "{processedAtColumn}" IS NULL
            AND "{partitionKeyColumn}" = @partition
            ORDER BY "{idColumn}"
            LIMIT @batchSize
            FOR UPDATE NOWAIT
            """;
    }

    protected abstract TEntity BuildEntityFromReader(DbDataReader reader);

    [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Executing SQL to fetch messages for partition {Partition}: {Sql}")]
    private partial void LogFetchSql(string Partition, string Sql);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Could not acquire lock for {Type} partition {Partition}, skipping processing")]
    private partial void LogCouldNotAcquireLock(string Type, string Partition, Exception exception);
}
