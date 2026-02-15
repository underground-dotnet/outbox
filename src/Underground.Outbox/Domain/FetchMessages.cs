using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Runtime.CompilerServices;

using Npgsql;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

internal sealed class FetchMessages<TEntity>(IDbContext dbContext) where TEntity : class, IMessage
{
#pragma warning disable S2743 // A static field in a generic type is not shared among instances of different close constructed types.
    private static readonly ConditionalWeakTable<IModel, string> SqlByModel = new();
#pragma warning restore S2743 // A static field in a generic type is not shared among instances of different close constructed types.

    internal async Task<List<TEntity>> ExecuteAsync(string partition, int batchSize, CancellationToken cancellationToken)
    {
        var sql = SqlByModel.GetValue(dbContext.Model, static model => BuildSql(model));

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

    private static string BuildSql(IModel model)
    {
        // dynamically extract table and column names to build the SQL query, since those can be overriden via EF Core mappings
        var entityType = model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException($"Entity type {typeof(TEntity)} not found in DbContext model.");
        var tableName = entityType.GetTableName() ?? throw new InvalidOperationException($"Table name for entity type {typeof(TEntity)} is not configured.");
        var schema = entityType.GetSchema();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{tableName}\"" : $"\"{schema}\".\"{tableName}\"";
        var tableIdentifier = StoreObjectIdentifier.Table(tableName, schema);

        var processedAtColumn = entityType.FindProperty(nameof(IMessage.ProcessedAt))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.ProcessedAt)} not found in entity type {typeof(TEntity)}.");
        var partitionKeyColumn = entityType.FindProperty(nameof(IMessage.PartitionKey))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.PartitionKey)} not found in entity type {typeof(TEntity)}.");
        var idColumn = entityType.FindProperty(nameof(IMessage.Id))?.GetColumnName(tableIdentifier)
            ?? throw new InvalidOperationException($"Property {nameof(IMessage.Id)} not found in entity type {typeof(TEntity)}.");

        return $"""
            SELECT *
            FROM {fullTableName}
            WHERE "{processedAtColumn}" IS NULL
            AND "{partitionKeyColumn}" = @partition
            ORDER BY "{idColumn}"
            LIMIT @batchSize
            FOR UPDATE NOWAIT
            """;
    }
}
