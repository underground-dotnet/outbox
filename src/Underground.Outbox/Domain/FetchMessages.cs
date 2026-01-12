using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class FetchMessages<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
    internal async Task<List<TEntity>> ExecuteAsync(string partition, int batchSize, CancellationToken cancellationToken)
    {
        // dynamically extract table and column names to build the SQL query, since those can be overriden via EF Core mappings
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException($"Entity type {typeof(TEntity)} not found in DbContext model.");
        var tableName = entityType.GetTableName();
        var schema = entityType.GetSchema();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";

        var processedAtColumn = entityType.FindProperty(nameof(IMessage.ProcessedAt))?.GetColumnName(StoreObjectIdentifier.Table(tableName!, schema)) ?? throw new InvalidOperationException($"Property {nameof(IMessage.ProcessedAt)} not found in entity type {typeof(TEntity)}.");
        var partitionKeyColumn = entityType.FindProperty(nameof(IMessage.PartitionKey))?.GetColumnName(StoreObjectIdentifier.Table(tableName!, schema)) ?? throw new InvalidOperationException($"Property {nameof(IMessage.PartitionKey)} not found in entity type {typeof(TEntity)}.");
        var idColumn = entityType.FindProperty(nameof(IMessage.Id))?.GetColumnName(StoreObjectIdentifier.Table(tableName!, schema)) ?? throw new InvalidOperationException($"Property {nameof(IMessage.Id)} not found in entity type {typeof(TEntity)}.");

        var sql = $"""
            SELECT *
            FROM {fullTableName}
            WHERE "{processedAtColumn}" IS NULL
            AND "{partitionKeyColumn}" = @partition
            ORDER BY "{idColumn}"
            LIMIT @batchSize
            FOR UPDATE NOWAIT
            """;

        try
        {
            return await dbContext.Set<TEntity>()
                .FromSqlRaw(
                    sql,
                    new NpgsqlParameter("partition", partition),
                    new NpgsqlParameter("batchSize", batchSize)
                )
                .ToListAsync(cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is PostgresException { SqlState: "55P03" }) // lock_not_available
        {
            // another processor is already handling messages for this partition
            return [];
        }
    }
}
